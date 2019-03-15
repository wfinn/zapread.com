﻿using LightningLib.lndrpc;
using Microsoft.AspNet.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using zapread.com.Database;
using zapread.com.Hubs;
using zapread.com.Models;
using System.Data.Entity;
using Microsoft.AspNet.Identity;
using System.Threading;
using zapread.com.Services;
using System.Net;
using zapread.com.Models.Database;

namespace zapread.com.Controllers
{
    [RoutePrefix("Lightning")]
    public class LightningController : Controller
    {
        /// <summary>
        /// This is the interface to a singleton payments service which is injected for IOC.
        /// </summary>
        public ILightningPayments paymentsService { get; private set; }

        /// <summary>
        /// Constructor with dependency injection for IOC and controller singleton control.
        /// </summary>
        /// <param name="paymentsService"></param>
        public LightningController(ILightningPayments paymentsService)
        {
            this.paymentsService = paymentsService;
        }

        private static ConcurrentDictionary<Guid, TransactionListener> lndTransactionListeners = new ConcurrentDictionary<Guid, TransactionListener>();

        //Used for rate limiting double withdraws
        static ConcurrentDictionary<string, DateTime> WithdrawRequests = new ConcurrentDictionary<string, DateTime>();

        // GET: Lightning
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<JsonResult> CheckPayment(string invoice, bool isDeposit = true)
        {
            // STEP 1: check the database
            using (ZapContext db = new ZapContext())
            {
                var p = await db.LightningTransactions
                    .FirstOrDefaultAsync(t => t.PaymentRequest == invoice);

                if (p != null)
                {
                    if (isDeposit && !p.IsDeposit)
                        return Json(new { success = false, message="invoice is not a deposit invoice" });

                    if (p.IsSettled)
                    {
                        return Json(new { success = true, result = true });
                    }
                    // Need to go on to check payment
                }
                else
                {
                    // We don't know this invoice
                    return Json(new { success = false, message = "invoice is not known to this node" });
                }

                // STEP 2: check our lightning node
                LndRpcClient lndClient = GetLndClient();

                // Decode the payment request
                var decoded = lndClient.DecodePayment(invoice);

                // Get the hash
                var hash = decoded.payment_hash;

                // GetInvoice expects the hash in base64 encoded format
                LightningLib.DataEncoders.HexEncoder h = new LightningLib.DataEncoders.HexEncoder();
                var hash_bytes = h.DecodeData(hash);
                var hash_b64 = Convert.ToBase64String(hash_bytes);

                if (isDeposit)
                {
                    var inv = lndClient.GetInvoice(hash_b64);
                    if (!inv.settled.HasValue || (inv.settled.HasValue && inv.settled.Value == false))
                    {
                        return Json(new { success = true, result=false });
                    }
                    if (inv.settled.HasValue && inv.settled.Value)
                    {
                        // TODO: check if invoice listeners are running - start if not running

                        // Use the standard receiving logic to send real time notification to clients
                        NotifyClientsInvoicePaid(inv);
                        return Json(new { success = true, result = true });
                    }
                }
                return Json(new { success = true });
            }
        }

        [HttpGet]
        [AllowAnonymous]
        [Route("GetInvoiceStatus/{request?}")]
        public ActionResult GetInvoiceStatus(string request)
        {
            using (ZapContext db = new ZapContext())
            {
                var tx = db.LightningTransactions.AsNoTracking().FirstOrDefault(t => t.PaymentRequest == request);
                return Json(tx, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Provide a new LN invoice with given parameters
        /// </summary>
        /// <param name="amount">number in Satoshi</param>
        /// <param name="memo">encoded in invoice</param>
        /// <param name="anon">flag to specify if invoice is unrelated to a user account</param>
        /// <param name="use">what the invoice is used for</param>
        /// <param name="useId">target of invoice (user id, post id, etc)</param>
        /// <returns>LnRequestInvoiceResponse object which contains Invoice field</returns>
        [HttpPost]
        public ActionResult GetDepositInvoice(string amount, string memo, string anon, string use, int? useId, int? useAction)
        {
            bool isAnon = !(anon == null || anon != "1");
            if (!isAnon && !User.Identity.IsAuthenticated)
            {
                // This is a user-related invoice, and no user is logged in.
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Url.ToString() });
            }

            string userId;
            if (isAnon)
            {
                userId = null;
            }
            else
            {
                userId = User.Identity.GetUserId();
            }

            if (memo == null || memo == "")
            {
                memo = "Zapread.com";
            }

            LndRpcClient lndClient = GetLndClient();

            var inv = lndClient.AddInvoice(Convert.ToInt64(amount), memo: memo, expiry: "3600");

            LnRequestInvoiceResponse resp = new LnRequestInvoiceResponse()
            {
                Invoice = inv.payment_request,
                Result = "success",
            };

            //Create transaction record (not settled)
            using (ZapContext db = new ZapContext())
            {
                // TODO: ensure user exists?
                User user = null;
                if (userId != null)
                {
                    user = db.Users.Where(u => u.AppId == userId).First();
                }
                TransactionUse usedFor = TransactionUse.Undefined;
                TransactionUseAction usedForAction = TransactionUseAction.Undefined;
                int usedForId = useId != null ? useId.Value : -1;
                if (use == "tip")
                {
                    usedFor = TransactionUse.Tip;
                }
                else if (use == "votePost")
                {
                    usedFor = TransactionUse.VotePost;
                }
                else if (use == "voteComment")
                {
                    usedFor = TransactionUse.VoteComment;
                }
                else if (use == "userDeposit")
                {
                    usedFor = TransactionUse.UserDeposit;
                    usedForId = userId != null ? user.Id : -1;
                }

                if (useAction != null)
                {
                    if (useAction.Value == 0)
                    {
                        usedForAction = TransactionUseAction.VoteDown;
                    }
                    else if (useAction.Value == 1)
                    {
                        usedForAction = TransactionUseAction.VoteUp;
                    }
                }

                //create a new transaction record in database
                LNTransaction t = new LNTransaction()
                {
                    User = user,
                    IsSettled = false,
                    IsSpent = false,
                    Memo = memo,
                    Amount = Convert.ToInt64(amount),
                    HashStr = inv.r_hash,
                    IsDeposit = true,
                    TimestampCreated = DateTime.Now,
                    PaymentRequest = inv.payment_request,
                    UsedFor = usedFor,
                    UsedForId = usedForId,
                    UsedForAction = usedForAction,
                };
                db.LightningTransactions.Add(t);
                db.SaveChanges();
                resp.Id = t.Id;
            }

            if (true) // debugging
            {
                // If a listener is not already running, this should start
                // Check if there is one already online.
                var numListeners = lndTransactionListeners.Count(kvp => kvp.Value.IsLive);

                // If we don't have one running - start it and subscribe
                if (numListeners < 1)
                {
                    var listener = lndClient.GetListener();
                    lndTransactionListeners.TryAdd(listener.ListenerId, listener);           // keep alive while we wait for payment
                    listener.InvoicePaid += NotifyClientsInvoicePaid;                        // handle payment message
                    listener.StreamLost += OnListenerLost;                                   // stream lost
                    var a = new Task(() => listener.Start());                                // listen for payment
                    a.Start();
                }
            }
            return Json(resp);
        }

        /// <summary>
        /// If the connection to LND is closed, this is triggered.  Remove our reference to the listener object for the stream.
        /// </summary>
        /// <param name="l"></param>
        private static void OnListenerLost(TransactionListener l)
        {
            lndTransactionListeners.TryRemove(l.ListenerId, out TransactionListener oldListener);
        }

        // We have received asynchronous notification that a lightning invoice has been paid
        private static void NotifyClientsInvoicePaid(Invoice invoice)
        {
            // Check if the invoice received was paid.  LND also sends updates 
            // for new invoices to the invoice stream.  We want to listen for settled invoices here.
            if (!invoice.settled.HasValue)
            {
                // Bad invoice
                // Todo - logging
                return;
            }
            if (!invoice.settled.Value)
            {
                // Optional - add some logic to check invoices on the stream.  These invoices
                // which are not settled are likely new deposit requests.  For the purposes of
                // this function, we only care about settled invoices.
                return;
            }
            var context = GlobalHost.ConnectionManager.GetHubContext<NotificationHub>();

            // Update LN transaction status in db
            using (ZapContext db = new ZapContext())
            {
                // Check if unsettled transaction exists in db matching the invoice that was just settled.
                var tx = db.LightningTransactions
                    .Include(tr => tr.User)
                    .Where(tr => tr.PaymentRequest == invoice.payment_request)
                    .ToList();
                
                DateTime settletime = DateTime.UtcNow;
                LNTransaction t;
                if (tx.Count > 0)
                {
                    // We found it - mark it as settled.
                    t = tx.First();
                    t.TimestampSettled = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Utc) + TimeSpan.FromSeconds(Convert.ToInt64(invoice.settle_date));
                    t.IsSettled = true;
                }
                else
                {
                    // This invoice is not in the db - it may not be related to this service.  
                    // We still record it in our database for any possible user forensics/history later.
                    settletime = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Utc) + TimeSpan.FromSeconds(Convert.ToInt64(invoice.settle_date));
                    t = new LNTransaction()
                    {
                        IsSettled = invoice.settled.Value,
                        Memo = invoice.memo,
                        Amount = Convert.ToInt64(invoice.value),
                        HashStr = invoice.r_hash,
                        IsDeposit = true,
                        TimestampSettled = settletime,
                        TimestampCreated = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Utc) + TimeSpan.FromSeconds(Convert.ToInt64(invoice.creation_date)),
                        PaymentRequest = invoice.payment_request,
                        User = null,
                    };
                    db.LightningTransactions.Add(t);
                }

                var user = t.User;
                double userBalance = 0.0;

                if (user == null)
                {
                    // this should not happen? - verify.  Maybe this is the case for transactions related to votes?
                    // throw new Exception("Error accessing user information related to settled LN Transaction.");
                }
                else
                {
                    // Update user balance - this is a deposit.
                    user.Funds.Balance += Convert.ToInt64(invoice.value);
                    userBalance = Math.Floor(user.Funds.Balance);
                }

                t.IsSettled = invoice.settled.Value;
                db.SaveChanges();

                // Send live signal to listening clients on websockets/SignalR
                context.Clients.All.NotifyInvoicePaid(new { invoice = invoice.payment_request, balance = userBalance, txid = t.Id });
            }
        }

        /// <summary>
        /// Pay a lightning invoice.
        /// </summary>
        /// <param name="request">The lightning invoice</param>
        /// <returns></returns>
        [HttpPost]
        public ActionResult SubmitPaymentRequest(string request)
        {
            var userId = User.Identity.GetUserId();

            if (userId == null)
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Request.Url.ToString() });
            }

            LndRpcClient lndClient = GetLndClient();

            string ip = GetClientIpAddress(Request);

            try
            {
                var paymentResult = paymentsService.TryWithdrawal(request, userId, ip, lndClient);
                return Json(paymentResult);
            }
            catch (Exception e)
            {
                MailingService.Send(new UserEmailModel()
                {
                    Destination = System.Configuration.ConfigurationManager.AppSettings["ExceptionReportEmail"],
                    Body = " Exception: " + e.Message + "\r\n Stack: " + e.StackTrace + "\r\n invoice: " + request + "\r\n user: " + userId,
                    Email = "",
                    Name = "zapread.com Exception",
                    Subject = "User withdraw error 1",
                });
                return Json(new { Result = "Error processing request." });
            }
        }

        private static LndRpcClient GetLndClient()
        {
            LndRpcClient lndClient;
            using (var db = new ZapContext())
            {
                var g = db.ZapreadGlobals.Where(gl => gl.Id == 1)
                    .AsNoTracking()
                    .FirstOrDefault();

                lndClient = new LndRpcClient(
                host: g.LnMainnetHost,
                macaroonAdmin: g.LnMainnetMacaroonAdmin,
                macaroonRead: g.LnMainnetMacaroonRead,
                macaroonInvoice: g.LnMainnetMacaroonInvoice);
            }

            return lndClient;
        }

        public static string GetClientIpAddress(HttpRequestBase request)
        {
            try
            {
                var userHostAddress = request.UserHostAddress;

                // Attempt to parse.  If it fails, we catch below and return "0.0.0.0"
                // Could use TryParse instead, but I wanted to catch all exceptions
                IPAddress.Parse(userHostAddress);

                var xForwardedFor = request.ServerVariables["X_FORWARDED_FOR"];

                if (string.IsNullOrEmpty(xForwardedFor))
                    return userHostAddress;

                // Get a list of public ip addresses in the X_FORWARDED_FOR variable
                var publicForwardingIps = xForwardedFor.Split(',').Where(ip => !IsPrivateIpAddress(ip)).ToList();

                // If we found any, return the last one, otherwise return the user host address

                var retval = publicForwardingIps.Any() ? publicForwardingIps.Last() : userHostAddress;

                return retval;
            }
            catch (Exception)
            {
                // Always return all zeroes for any failure (my calling code expects it)
                return "0.0.0.0";
            }
        }

        private static bool IsPrivateIpAddress(string ipAddress)
        {
            // http://en.wikipedia.org/wiki/Private_network
            // Private IP Addresses are: 
            //  24-bit block: 10.0.0.0 through 10.255.255.255
            //  20-bit block: 172.16.0.0 through 172.31.255.255
            //  16-bit block: 192.168.0.0 through 192.168.255.255
            //  Link-local addresses: 169.254.0.0 through 169.254.255.255 (http://en.wikipedia.org/wiki/Link-local_address)

            var ip = IPAddress.Parse(ipAddress);
            var octets = ip.GetAddressBytes();

            var is24BitBlock = octets[0] == 10;
            if (is24BitBlock) return true; // Return to prevent further processing

            var is20BitBlock = octets[0] == 172 && octets[1] >= 16 && octets[1] <= 31;
            if (is20BitBlock) return true; // Return to prevent further processing

            var is16BitBlock = octets[0] == 192 && octets[1] == 168;
            if (is16BitBlock) return true; // Return to prevent further processing

            var isLinkLocalAddress = octets[0] == 169 && octets[1] == 254;
            return isLinkLocalAddress;
        }
    }
}