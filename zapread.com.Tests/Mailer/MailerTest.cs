﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using zapread.com.Controllers;
using zapread.com.Models;
using zapread.com.Models.Database;

namespace zapread.com.Tests.Mailer
{
    [TestClass]
    public class MailerTest
    {
        [TestMethod]
        public void TestGenerate()
        {
            var request = new Mock<HttpRequestBase>();
            var mock = new Mock<ControllerContext>();

            request.SetupGet(x => x.Headers).Returns(
                new System.Net.WebHeaderCollection {
                    {"X-Requested-With", "XMLHttpRequest"}
                });

            var context = new Mock<HttpContextBase>();
            context.SetupGet(x => x.Request).Returns(request.Object);

            mock.SetupGet(p => p.HttpContext.User.Identity.Name).Returns("test");
            if ("test" != null)
            {
                mock.SetupGet(p => p.HttpContext.Request.IsAuthenticated).Returns(true);
                mock.SetupGet(p => p.HttpContext.User.Identity.IsAuthenticated).Returns(true);
            }
            //else
            //{
            //    mock.SetupGet(p => p.HttpContext.Request.IsAuthenticated).Returns(false);
            //}

            var routeData = new RouteData();
            routeData.Values.Add("controller", "MessagesController");
            routeData.Values.Add("action", "GetMessage");
            mock.SetupGet(m => m.RouteData).Returns(routeData);

            var view = new Mock<IView>();
            var engine = new Mock<IViewEngine>();
            var viewEngineResult = new ViewEngineResult(view.Object, engine.Object);
            engine.Setup(e => e.FindPartialView(It.IsAny<ControllerContext>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(viewEngineResult);
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(engine.Object);

            MessagesController controller = new MessagesController();

            controller.ControllerContext = new ControllerContext(context.Object, routeData, controller);

            var vm = new ChatMessageViewModel()
            {
                From = new Models.Database.User() { },
                To = new Models.Database.User() { },
                IsReceived = true,
                Message = new UserMessage() { From = new Models.Database.User() { Name = "FromSender", AppId = "123" }, TimeStamp = DateTime.Now, Content = "This is a test message!" },
            };

            var HTMLString = controller.RenderPartialViewToString(viewName: "_PartialChatMessage", model: vm);

            var pm = new PreMailer.Net.PreMailer(HTMLString);
        }
    }
}
