﻿using LightningLib.lndrpc;
using LightningLib.lndrpc.Exceptions;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using zapread.com.Database;
using zapread.com.Models;

namespace zapread.com.Services
{
    public class LNTransactionMonitor
    {
        public void CheckLNTransactions()
        {
            using (var db = new ZapContext())
            {
                var website = db.ZapreadGlobals.Where(gl => gl.Id == 1)
                    .AsNoTracking()
                    .FirstOrDefault();

                LndRpcClient lndClient = new LndRpcClient(
                    host: website.LnMainnetHost,
                    macaroonAdmin: website.LnMainnetMacaroonAdmin,
                    macaroonRead: website.LnMainnetMacaroonRead,
                    macaroonInvoice: website.LnMainnetMacaroonInvoice);

                // These are the unpaid invoices in database
                var unpaidInvoices = db.LightningTransactions
                    .Where(t => t.IsSettled == false)
                    .Where(t => t.IsDeposit == true)
                    .Where(t => t.IsIgnored == false)
                    .Include(t => t.User)
                    .Include(t => t.User.Funds);

                //var invoiceDebug = unpaidInvoices.ToList();
                foreach (var i in unpaidInvoices)
                {
                    if (i.HashStr != null)
                    {
                        var inv = lndClient.GetInvoice(rhash: i.HashStr);
                        if (inv != null && inv.settled != null && inv.settled == true)
                        {
                            // Paid but not applied in DB
                            var use = i.UsedFor;
                            if (use == TransactionUse.VotePost)
                            {
                                //if (false) // Disable for now
                                //{
                                //    var vc = new VoteController();
                                //    var v = new VoteController.Vote()
                                //    {
                                //        a = Convert.ToInt32(i.Amount),
                                //        d = i.UsedForAction == TransactionUseAction.VoteDown ? 0 : 1,
                                //        Id = i.UsedForId,
                                //        tx = i.Id
                                //    };
                                //    await vc.Post(v);

                                //    i.IsSpent = true;
                                //    i.IsSettled = true;
                                //    i.TimestampSettled = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(inv.settle_date)).UtcDateTime;
                                //}
                            }
                            else if (use == TransactionUse.VoteComment)
                            {
                                ;
                            }
                            else if (use == TransactionUse.UserDeposit)
                            {
                                if (i.User == null)
                                {
                                    // Not sure how to deal with this other than add funds to Community
                                    website.CommunityEarnedToDistribute += i.Amount;
                                    i.IsSpent = true;
                                    i.IsSettled = true;
                                    i.TimestampSettled = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(inv.settle_date)).UtcDateTime;
                                }
                                else
                                {
                                    // Deposit funds in user account
                                    var user = i.User;
                                    user.Funds.Balance += i.Amount;
                                    i.IsSettled = true;
                                    i.TimestampSettled = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(inv.settle_date)).UtcDateTime;

                                }
                            }
                            else if (use == TransactionUse.Undefined)
                            {
                                if (i.User == null)
                                {
                                    // Not sure how to deal with this other than add funds to Community
                                    website.CommunityEarnedToDistribute += i.Amount;
                                    i.IsSpent = true;
                                    i.IsSettled = true;
                                    i.TimestampSettled = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(inv.settle_date)).UtcDateTime;
                                }
                                else
                                {
                                    // Not sure what the user was doing - deposit into their account.
                                    ;
                                }
                            }
                        }
                        else if (inv != null)
                        {
                            // Not settled - check expiry
                            var t1 = Convert.ToInt64(inv.creation_date);
                            var tNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            var tExpire = t1 + Convert.ToInt64(inv.expiry) + 10000; //Add a buffer time
                            if (tNow > tExpire)
                            {
                                // Expired - let's stop checking this invoice
                                i.IsIgnored = true;
                            }
                            else
                            {
                                ; // keep waiting
                            }
                        }
                    }
                    else
                    {
                        // No hash string to look it up.  Must be an error somewhere.
                        i.IsIgnored = true;
                    }
                }

                db.SaveChanges();

                // These are non-settled withdraws in the database
                var unpaidWithdraws = db.LightningTransactions
                    .Where(t => t.IsSettled == false)   // Not settled
                    .Where(t => t.IsDeposit == false)   // Withdraw
                    .Where(t => t.IsIgnored == false)   // Still valid
                    .Include(t => t.User)
                    .Include(t => t.User.Funds);

                var numup = unpaidWithdraws.Count();

                if (numup > 0)
                {
                    // Check the unpaid withdraws
                    var payments = lndClient.GetPayments();
                    foreach (var i in unpaidWithdraws)
                    {
                        var pmt = payments.payments.Where(p => p.payment_hash == i.HashStr).FirstOrDefault();

                        if (pmt != null)
                        {
                            // Paid?
                            ;
                            if (i.ErrorMessage == "Error: invoice is already paid")
                            {
                                // This was a duplicate payment - funds were not sent and this payment hash should only have one paid version.
                                i.IsIgnored = true;
                            }
                            else if (i.ErrorMessage == "Error executing payment.")
                            {
                                // Didn't get paid!
                                //i.IsIgnored = true;
                                ;
                            }
                            else if (i.ErrorMessage == "Error: payment is in transition")
                            {
                                // Double spend attempt stopped.  No loss of funds
                                i.IsIgnored = true;
                            }
                            else if (i.ErrorMessage == "Error: FinalExpiryTooSoon")
                            {
                                i.IsIgnored = true;
                            }
                            else if (i.ErrorMessage == "Error validating payment.")
                            {
                                // Payment has come through
                                double amount = Convert.ToDouble(i.Amount);

                                // No longer in limbo
                                i.User.Funds.LimboBalance -= amount;
                                if (i.User.Funds.LimboBalance < 0)
                                {
                                    // Should not happen!
                                    i.User.Funds.LimboBalance = 0;
                                }
                                i.IsIgnored = true;

                                MailingService.Send(new UserEmailModel()
                                {
                                    Destination = System.Configuration.ConfigurationManager.AppSettings["ExceptionReportEmail"],
                                    Body = "Withdraw Invoice completed limbo (payment was found)."
                                                + "\r\n invoice: " + i.PaymentRequest
                                                + "\r\n user: " + i.User.Name + "(" + i.User.AppId + ")"
                                                + "\r\n amount: " + Convert.ToString(i.Amount)
                                                + "\r\n error: " + i.ErrorMessage == null ? "null" : i.ErrorMessage,
                                    Email = "",
                                    Name = "zapread.com Monitoring",
                                    Subject = "User withdraw limbo complete",
                                });
                            }
                            else
                            {
                                // Payment may have gone through without recording in DB.
                                ;
                                if (i.ErrorMessage != null)
                                {
                                    i.IsError = true;
                                }
                                i.IsSettled = true;
                            }
                        }
                        else
                        {
                            double amount = Convert.ToDouble(i.Amount);
                            // Consider as not paid (for now) if not in DB - probably an error
                            if (i.ErrorMessage == "Error: invoice is already paid")
                            {
                                // This was a duplicate payment - funds were not sent and this payment hash should only have one paid version.
                                i.IsIgnored = true;
                            }
                            if (i.ErrorMessage == "Error: amount must be specified when paying a zero amount invoice")
                            {
                                i.IsIgnored = true;
                                if (i.User.Funds.LimboBalance - amount < 0)
                                {
                                    if (i.User.Funds.LimboBalance < 0) // shouldn't happen!
                                    {
                                        i.User.Funds.LimboBalance = 0;
                                    }
                                    i.User.Funds.Balance += i.User.Funds.LimboBalance;
                                    i.User.Funds.LimboBalance = 0;
                                }
                                else
                                {
                                    i.User.Funds.LimboBalance -= amount;
                                    i.User.Funds.Balance += amount;
                                    if (i.User.Funds.LimboBalance < 0) // shouldn't happen!
                                    {
                                        i.User.Funds.LimboBalance = 0;
                                    }
                                }
                            }
                            else
                            {
                                var inv = lndClient.DecodePayment(i.PaymentRequest);
                                var t1 = i.TimestampCreated.Value;
                                var tNow = DateTime.UtcNow;// DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                                var tExpire = t1.AddSeconds(Convert.ToInt64(inv.expiry) + 10000); //Add a buffer time
                                if (tNow > tExpire)
                                {
                                    // Expired - let's stop checking this invoice
                                    i.IsIgnored = true;
                                    if (i.ErrorMessage == "Error validating payment.")
                                    {
                                        // The payment can't go through any longer.
                                        if (i.User.Funds.LimboBalance - amount < 0)
                                        {
                                            //shouldn't happen!
                                            if (i.User.Funds.LimboBalance < 0)
                                            {
                                                i.User.Funds.LimboBalance = 0;
                                            }
                                            i.User.Funds.Balance += i.User.Funds.LimboBalance;
                                            i.User.Funds.LimboBalance = 0;
                                        }
                                        else
                                        {
                                            i.User.Funds.LimboBalance -= amount;
                                            i.User.Funds.Balance += amount;
                                        }
                                        
                                        MailingService.Send(new UserEmailModel()
                                        {
                                            Destination = System.Configuration.ConfigurationManager.AppSettings["ExceptionReportEmail"],
                                            Body = "Withdraw Invoice expired (payment not found). Funds released to user."
                                                + "\r\n invoice: " + i.PaymentRequest
                                                + "\r\n user: " + i.User.Name + "(" + i.User.AppId + ")"
                                                + "\r\n amount: " + Convert.ToString(i.Amount)
                                                + "\r\n error: " + i.ErrorMessage == null ? "null" : i.ErrorMessage,
                                            Email = "",
                                            Name = "zapread.com Monitoring",
                                            Subject = "User withdraw limbo expired",
                                        });

                                        // TODO: send user email notification update of result.
                                    }
                                }
                                else
                                {
                                    ; // keep waiting
                                }
                            }
                        }
                    }
                }
                db.SaveChanges();
            }
        }
    }
}