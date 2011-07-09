﻿// ReSharper disable RedundantUsingDirective

using System;
using System.Collections.Generic;
using AutoMoq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Model.Xbmc;
using NzbDrone.Core.Providers;
using NzbDrone.Core.Providers.Core;
using NzbDrone.Core.Providers.Xbmc;
using NzbDrone.Core.Repository;
using NzbDrone.Core.Repository.Quality;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test
{
    [TestFixture]
    // ReSharper disable InconsistentNaming
    public class EventClientProviderTest : TestBase
    {
        [Test]
        public void SendNotification_true()
        {
            //Setup
            var mocker = new AutoMoqer();

            var header = "NzbDrone Test";
            var message = "Test Message!";
            var address = "localhost";

            var fakeUdp = mocker.GetMock<UdpProvider>();
            fakeUdp.Setup(s => s.Send(address, UdpProvider.PacketType.Notification, It.IsAny<byte[]>())).Returns(true);

            //Act
            var result = mocker.Resolve<EventClientProvider>().SendNotification(header, message, IconType.Jpeg, "NzbDrone.jpg", address);

            //Assert
            Assert.AreEqual(true, result);
        }

        [Test]
        public void SendNotification_false()
        {
            //Setup
            var mocker = new AutoMoqer();

            var header = "NzbDrone Test";
            var message = "Test Message!";
            var address = "localhost";

            var fakeUdp = mocker.GetMock<UdpProvider>();
            fakeUdp.Setup(s => s.Send(address, UdpProvider.PacketType.Notification, It.IsAny<byte[]>())).Returns(false);

            //Act
            var result = mocker.Resolve<EventClientProvider>().SendNotification(header, message, IconType.Jpeg, "NzbDrone.jpg", address);

            //Assert
            Assert.AreEqual(false, result);
        }

        [Test]
        public void SendAction_Update_true()
        {
            //Setup
            var mocker = new AutoMoqer();

            var path = @"C:\Test\TV\30 Rock";
            var command = String.Format("ExecBuiltIn(UpdateLibrary(video,{0}))", path);
            var address = "localhost";

            var fakeUdp = mocker.GetMock<UdpProvider>();
            fakeUdp.Setup(s => s.Send(address, UdpProvider.PacketType.Action, It.IsAny<byte[]>())).Returns(true);

            //Act
            var result = mocker.Resolve<EventClientProvider>().SendAction(address, ActionType.ExecBuiltin, command);

            //Assert
            Assert.AreEqual(true, result);
        }

        [Test]
        public void SendAction_Update_false()
        {
            //Setup
            var mocker = new AutoMoqer();

            var path = @"C:\Test\TV\30 Rock";
            var command = String.Format("ExecBuiltIn(UpdateLibrary(video,{0}))", path);
            var address = "localhost";

            var fakeUdp = mocker.GetMock<UdpProvider>();
            fakeUdp.Setup(s => s.Send(address, UdpProvider.PacketType.Action, It.IsAny<byte[]>())).Returns(false);

            //Act
            var result = mocker.Resolve<EventClientProvider>().SendAction(address, ActionType.ExecBuiltin, command);

            //Assert
            Assert.AreEqual(false, result);
        }
    }
}