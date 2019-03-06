﻿// enable vibration support
navigator.vibrate = navigator.vibrate || navigator.webkitVibrate || navigator.mozVibrate || navigator.msVibrate;

$(document).ready(function () {

    // Streaming updates
    var hub = $.connection.notificationHub;

    hub.client.SendUserMessage = function (envelope) {
        console.log(envelope.message);
        console.log(envelope.clickUrl);
        if (typeof ChattingWithId !== 'undefined') {
            // Skip
        } else {
            toastr.options.onclick = function () {
                console.log('clicked');
                window.open(envelope.clickUrl, '_blank');
            };

            if (envelope.hasReason) {
                toastr.success(envelope.message, envelope.reason);
            } else {
                toastr.success(envelope.message, 'Message Received');
            }
        }
        
    };

    hub.client.SendUserChat = function (envelope) {
        if (typeof ChattingWithId !== 'undefined') {
            if (envelope.reason === ChattingWithId) {
                $("#endMessages").append(envelope.message);
                $('.postTime').each(function (i, e) {
                    var time = moment.utc($(e).html()).local().calendar();
                    var date = moment.utc($(e).html()).local().format("DD MMM YYYY");
                    $(e).html('<span>' + time + ' - ' + date + '</span>');
                    $(e).css('display', 'inline');
                    $(e).removeClass("postTime");
                });
                window.scrollTo(0, document.body.scrollHeight + 10);
            }
        }
    };

    hub.client.NotifyInvoicePaid = function (invoiceResponse) {
        if (invoiceResponse.invoice === $("#lightningDepositInvoiceInput").val()) {
            $("#lightningDepositInvoiceResult").html("Successfully received deposit.");
            $("#lightningDepositInvoiceResult").removeClass("bg-error");
            $("#lightningDepositInvoiceResult").removeClass("bg-info");
            $("#lightningDepositInvoiceResult").removeClass("bg-muted");
            $("#lightningDepositInvoiceResult").addClass("bg-success");
            $("#getInvoice").html("Get Invoice");    // Change button text from get invoice to update
            $("#lightningDepositInvoiceResult").show();
            $("#lightningDepositQR").hide();
            $("#lightningDepositInvoice").hide();

            if (navigator.vibrate) {
                // vibration API supported
                navigator.vibrate(300);
            }

            $(".userBalanceValue").each(function (i, e) {
                $(e).html(invoiceResponse.balance);
            });

            $(".partialContents").each(function (index, item) {
                var url = $(item).data("url");
                if (url && url.length > 0 && url === "/Account/Balance") {
                    $(item).load(url);
                }
            });

            // Update global var
            ub = invoiceResponse.balance;

            var userBalance = ub;
            $('#userDepositBalance').html(userBalance);
            $('#userVoteBalance').html(userBalance);
            $.get("/Account/GetBalance", function (data, status) {
                $(".userBalanceValue").each(function (i, e) {
                    $(e).html(data.balance);
                });
            });
            
            $('#depositModal').modal('hide');

            try {
                lightningTable.ajax.reload(null, false);
            }
            catch (err) {
                console.log("couldn't refresh lightningTable");
            }
        }

        // Check if vote
        if (invoiceResponse.invoice === $("#voteDepositInvoiceInput").val()) {
            // Ok, the user paid the invoice.  Now we need to claim the vote.
            // If this transaction id is not found, or already claimed, the vote will not work.
            userVote.tx = invoiceResponse.txid;

            if (navigator.vibrate) {
                // vibration API supported
                navigator.vibrate(300);
            }

            if (isTip) {
                console.log('tip paid');
                doTip(userVote.id, userVote.amount, userVote.tx);
            }
            else {
                doVote(userVote.id, userVote.d, userVote.t, userVote.amount, userVote.tx);
            }
        }
    };

    $.connection.hub.start({
        waitForPageLoad: false
        }, function () {
            var cn = this;
            $(window).bind("beforeunload", function () {
                cn.stop();
            });
        })
        .done(function () {
            console.log("Hub Connected!, transport = " + $.connection.hub.transport.name);
        })
        .fail(function () {
            console.log("Could not Connect!");
        });
});