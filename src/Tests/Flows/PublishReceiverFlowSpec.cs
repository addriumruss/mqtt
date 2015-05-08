﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hermes;
using Hermes.Flows;
using Hermes.Packets;
using Hermes.Storage;
using Moq;
using Xunit;

namespace Tests.Flows
{
	public class PublishReceiverFlowSpec
	{
		[Fact]
		public async Task when_sending_publish_with_qos0_then_publish_is_sent_to_subscribers_and_no_ack_is_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { MaximumQualityOfService = QualityOfService.ExactlyOnce };
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object,
				publishSenderFlow.Object, retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var subscribedClientId1 = Guid.NewGuid().ToString();
			var subscribedClientId2 = Guid.NewGuid().ToString();
			var requestedQoS1 = QualityOfService.AtLeastOnce;
			var requestedQoS2 = QualityOfService.ExactlyOnce;
			var sessions = new List<ClientSession> { 
				new ClientSession {
					ClientId = subscribedClientId1,
					Clean = false,
					Subscriptions = new List<ClientSubscription> { 
						new ClientSubscription { ClientId = subscribedClientId1, 
							MaximumQualityOfService = requestedQoS1, TopicFilter = topic }}
				},
				new ClientSession {
					ClientId = subscribedClientId2,
					Clean = false,
					Subscriptions = new List<ClientSubscription> { 
						new ClientSubscription { ClientId = subscribedClientId2, 
							MaximumQualityOfService = requestedQoS2, TopicFilter = topic }}
				}
			};

			var client1Receiver = new Subject<IPacket> ();
			var client1Channel = new Mock<IChannel<IPacket>> ();

			client1Channel.Setup (c => c.Receiver).Returns (client1Receiver);

			var client2Receiver = new Subject<IPacket> ();
			var client2Channel = new Mock<IChannel<IPacket>> ();

			client2Channel.Setup (c => c.Receiver).Returns (client2Receiver);

			topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns (sessions.AsQueryable());

			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId1)))
				.Returns (client1Channel.Object);
			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId2)))
				.Returns (client2Channel.Object);

			var publish = new Publish (topic, QualityOfService.AtMostOnce, retain: false, duplicated: false);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			await flow.ExecuteAsync (clientId, publish, channel.Object);

			retainedRepository.Verify (r => r.Create (It.IsAny<RetainedMessage> ()), Times.Never);
			publishSenderFlow.Verify (s => s.SendPublishAsync (It.Is<string>(x => x == subscribedClientId1), 
				It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload)),
				It.Is<IChannel<IPacket>>(c => c == client1Channel.Object), It.Is<PendingMessageStatus>(x => x == PendingMessageStatus.PendingToSend)));
			publishSenderFlow.Verify (s => s.SendPublishAsync (It.Is<string>(x => x == subscribedClientId2), 
				It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload)),
				It.Is<IChannel<IPacket>>(c => c == client2Channel.Object), It.Is<PendingMessageStatus>(x => x == PendingMessageStatus.PendingToSend)));
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket> ()), Times.Never);
		}

		[Fact]
		public async Task when_sending_publish_with_qos1_then_publish_is_sent_to_subscribers_and_publish_ack_is_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { MaximumQualityOfService = QualityOfService.ExactlyOnce };
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object, 
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var subscribedClientId = Guid.NewGuid().ToString();
			var requestedQoS = QualityOfService.ExactlyOnce;
			var sessions = new List<ClientSession> { new ClientSession {
				ClientId = subscribedClientId,
				Clean = false,
				Subscriptions = new List<ClientSubscription> { 
					new ClientSubscription { ClientId = subscribedClientId, MaximumQualityOfService = requestedQoS, TopicFilter = topic }
				}
			}};

			var clientReceiver = new Subject<IPacket> ();
			var clientChannel = new Mock<IChannel<IPacket>> ();

			clientChannel.Setup (c => c.Receiver).Returns (clientReceiver);

			topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns ( sessions.AsQueryable());

			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId)))
				.Returns (clientChannel.Object);

			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var publish = new Publish (topic, QualityOfService.AtLeastOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.IsConnected).Returns (true);
			channel.Setup (c => c.Receiver).Returns (receiver);

			await flow.ExecuteAsync (clientId, publish, channel.Object);

			retainedRepository.Verify (r => r.Create (It.IsAny<RetainedMessage> ()), Times.Never);
			publishSenderFlow.Verify (s => s.SendPublishAsync (It.Is<string>(x => x == subscribedClientId), 
				It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload)),
				It.Is<IChannel<IPacket>>(c => c == clientChannel.Object), It.Is<PendingMessageStatus>(x => x == PendingMessageStatus.PendingToSend)));
			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishAck && 
				(p as PublishAck).PacketId == packetId.Value)));
		}

		[Fact]
		public void when_sending_publish_with_qos2_then_publish_is_sent_to_subscribers_and_publish_received_is_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { MaximumQualityOfService = QualityOfService.ExactlyOnce };
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			publishSenderFlow.Setup (f => f.SendPublishAsync (It.IsAny<string> (), It.IsAny<Publish> (), It.IsAny <IChannel<IPacket>> (), It.IsAny<PendingMessageStatus> ()))
				.Returns(Task.Delay(0));

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object, 
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var subscribedClientId = Guid.NewGuid().ToString();
			var requestedQoS = QualityOfService.ExactlyOnce;
			var sessions = new List<ClientSession> { new ClientSession {
				ClientId = subscribedClientId,
				Clean = false,
				Subscriptions = new List<ClientSubscription> { 
					new ClientSubscription { ClientId = subscribedClientId, MaximumQualityOfService = requestedQoS, TopicFilter = topic }
				}
			}};

			var clientReceiver = new Subject<IPacket> ();
			var clientChannel = new Mock<IChannel<IPacket>> ();

			clientChannel.Setup (c => c.Receiver).Returns (clientReceiver);

			topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns ( sessions.AsQueryable());

			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId)))
				.Returns (clientChannel.Object);

			var publish = new Publish (topic, QualityOfService.ExactlyOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Flow Test");

			var receiver = new Subject<IPacket> ();
			var channelMock = new Mock<IChannel<IPacket>> ();

			channelMock.Setup (c => c.IsConnected).Returns (true);
			channelMock.Setup (c => c.Receiver).Returns (receiver);

			var channel = channelMock.Object;

			var exception = Assert.Throws<AggregateException>(() => flow.ExecuteAsync (clientId, publish, channel).Wait());

			Assert.True (exception.InnerException is ProtocolException);
			publishSenderFlow.Verify (s => s.SendPublishAsync (It.Is<string>(x => x == subscribedClientId), 
				It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload)),
				It.Is<IChannel<IPacket>>(c => c == clientChannel.Object), It.Is<PendingMessageStatus>(x => x == PendingMessageStatus.PendingToSend)));
			retainedRepository.Verify (r => r.Create (It.IsAny<RetainedMessage> ()), Times.Never);
			channelMock.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishReceived && (p as PublishReceived).PacketId == packetId.Value)));
		}

		[Fact]
		public void when_sending_publish_with_qos2_and_no_release_is_sent_after_receiving_publish_received_then_publish_received_is_re_transmitted()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { 
				MaximumQualityOfService = QualityOfService.ExactlyOnce,
				WaitingTimeoutSecs = 1
			};
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var subscribedClientId = Guid.NewGuid().ToString();
			var requestedQoS = QualityOfService.ExactlyOnce;
			var sessions = new List<ClientSession> { new ClientSession {
				ClientId = subscribedClientId,
				Clean = false,
				Subscriptions = new List<ClientSubscription> { 
					new ClientSubscription { ClientId = subscribedClientId, MaximumQualityOfService = requestedQoS, TopicFilter = topic }
				}
			}};

			var clientReceiver = new Subject<IPacket> ();
			var clientChannel = new Mock<IChannel<IPacket>> ();

			clientChannel.Setup (c => c.Receiver).Returns (clientReceiver);

			topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns ( sessions.AsQueryable());

			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var publish = new Publish (topic, QualityOfService.ExactlyOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.IsConnected).Returns (true);
			channel.Setup (c => c.Receiver).Returns (receiver);

			var aggregateEx = Assert.Throws<AggregateException>(() => flow.ExecuteAsync (clientId, publish, channel.Object).Wait());

			Thread.Sleep (2000);

			Assert.NotNull (aggregateEx);
			Assert.True (aggregateEx.InnerException is ProtocolException);
			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishReceived 
				&& (p as PublishReceived).PacketId == packetId)), Times.AtLeast(2));
		}

		//[Fact]
		//public async Task when_sending_publish_with_qos2_and_release_is_sent_after_receiving_publish_received_then_publish_received_is_not_re_transmitted()
		//{
		//	var clientId = Guid.NewGuid ().ToString ();

		//	var configuration = new ProtocolConfiguration { 
		//		MaximumQualityOfService = QualityOfService.ExactlyOnce,
		//		WaitingTimeoutSecs = 2
		//	};
		//	var topicEvaluator = new Mock<ITopicEvaluator> ();
		//	var connectionProvider = new Mock<IConnectionProvider> ();
		//	var publishSenderFlow = new Mock<IPublishSenderFlow> ();
		//	var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
		//	var sessionRepository = new Mock<IRepository<ClientSession>> ();

		//	sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
		//		.Returns (new ClientSession {
		//			ClientId = clientId,
		//			PendingMessages = new List<PendingMessage> { new PendingMessage() }
		//		});

		//	var packetIdentifierRepository = Mock.Of<IRepository<PacketIdentifier>> ();

		//	var topic = "foo/bar";

		//	var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
		//		retainedRepository.Object, sessionRepository.Object, packetIdentifierRepository, configuration);

		//	var subscribedClientId = Guid.NewGuid().ToString();
		//	var requestedQoS = QualityOfService.ExactlyOnce;
		//	var sessions = new List<ClientSession> { new ClientSession {
		//		ClientId = subscribedClientId,
		//		Clean = false,
		//		Subscriptions = new List<ClientSubscription> { 
		//			new ClientSubscription { ClientId = subscribedClientId, MaximumQualityOfService = requestedQoS, TopicFilter = topic }
		//		}
		//	}};

		//	var clientReceiver = new Subject<IPacket> ();
		//	var clientChannel = new Mock<IChannel<IPacket>> ();

		//	clientChannel.Setup (c => c.Receiver).Returns (clientReceiver);

		//	topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
		//	sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns ( sessions.AsQueryable());

		//	var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
		//	var publish = new Publish (topic, QualityOfService.ExactlyOnce, retain: false, duplicated: false, packetId: packetId);

		//	publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

		//	var receiver = new Subject<IPacket> ();
		//	var sender = new Subject<IPacket> ();
		//	var channel = new Mock<IChannel<IPacket>> ();

		//	sender.OfType<PublishReceived>().Subscribe (received => {
		//		receiver.OnNext (new PublishRelease (received.PacketId));
		//	});

		//	channel.Setup (c => c.IsConnected).Returns (true);
		//	channel.Setup (c => c.Receiver).Returns (receiver);
		//	channel.Setup (c => c.Sender).Returns (sender);
		//	channel.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
		//		.Callback<IPacket> (packet => sender.OnNext (packet))
		//		.Returns(Task.Delay(0));

		//	//TODO: Fix this
		//	await flow.ExecuteAsync (clientId, publish, channel.Object);

		//	Thread.Sleep (2000);

		//	channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishReceived 
		//		&& (p as PublishReceived).PacketId == packetId)), Times.Once);
		//}

		[Fact]
		public async Task when_sending_publish_with_retain_then_retain_message_is_created()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> ();
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var sessions = new List<ClientSession> { new ClientSession { ClientId = Guid.NewGuid ().ToString (), Clean = false }};

			retainedRepository.Setup (r => r.Get (It.IsAny<Expression<Func<RetainedMessage, bool>>>())).Returns (default(RetainedMessage));
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns ( sessions.AsQueryable());

			var qos = QualityOfService.AtMostOnce;
			var payload = "Publish Flow Test";
			var publish = new Publish (topic, qos, retain: true, duplicated: false);

			publish.Payload = Encoding.UTF8.GetBytes (payload);

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			await flow.ExecuteAsync (clientId, publish, channel.Object);

			retainedRepository.Verify (r => r.Create (It.Is<RetainedMessage> (m => m.Topic == topic && m.QualityOfService == qos && m.Payload.ToList().SequenceEqual(publish.Payload))));
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket> ()), Times.Never);
		}

		[Fact]
		public async Task when_sending_publish_with_retain_then_retain_message_is_replaced()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> ();
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var sessions = new List<ClientSession> { new ClientSession { ClientId = Guid.NewGuid().ToString(), Clean = false }};

			var existingRetainedMessage = new RetainedMessage();

			retainedRepository.Setup (r => r.Get (It.IsAny<Expression<Func<RetainedMessage, bool>>>())).Returns (existingRetainedMessage);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>> ())).Returns (sessions.AsQueryable());

			var qos = QualityOfService.AtMostOnce;
			var payload = "Publish Flow Test";
			var publish = new Publish (topic, qos, retain: true, duplicated: false);

			publish.Payload = Encoding.UTF8.GetBytes (payload);

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			await flow.ExecuteAsync (clientId, publish, channel.Object);

			retainedRepository.Verify (r => r.Delete (It.Is<RetainedMessage> (m => m == existingRetainedMessage)));
			retainedRepository.Verify (r => r.Create (It.Is<RetainedMessage> (m => m.Topic == topic && m.QualityOfService == qos && m.Payload.ToList().SequenceEqual(publish.Payload))));
			channel.Verify (c => c.SendAsync (It.IsAny<IPacket> ()), Times.Never);
		}

		[Fact]
		public async Task when_sending_publish_with_qos_higher_than_supported_then_supported_is_used()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { MaximumQualityOfService = QualityOfService.AtLeastOnce };
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object, 
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var subscribedClientId = Guid.NewGuid().ToString();
			var requestedQoS = QualityOfService.ExactlyOnce;
			var sessions = new List<ClientSession> { new ClientSession {
				ClientId = subscribedClientId,
				Clean = false,
				Subscriptions = new List<ClientSubscription> { 
					new ClientSubscription { ClientId = subscribedClientId, MaximumQualityOfService = requestedQoS, TopicFilter = topic }
				}
			}};

			var clientReceiver = new Subject<IPacket> ();
			var clientChannel = new Mock<IChannel<IPacket>> ();

			clientChannel.Setup (c => c.Receiver).Returns (clientReceiver);

			topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>> ())).Returns (sessions.AsQueryable());

			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId)))
				.Returns (clientChannel.Object);

			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var publish = new Publish (topic, QualityOfService.ExactlyOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.IsConnected).Returns (true);
			channel.Setup (c => c.Receiver).Returns (receiver);

			await flow.ExecuteAsync (clientId, publish, channel.Object);

			publishSenderFlow.Verify (s => s.SendPublishAsync (It.Is<string>(x => x == subscribedClientId), 
				It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload)),
				It.Is<IChannel<IPacket>>(c => c == clientChannel.Object), It.Is<PendingMessageStatus>(x => x == PendingMessageStatus.PendingToSend)));
			retainedRepository.Verify(r => r.Create (It.IsAny<RetainedMessage> ()), Times.Never);
			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishAck && (p as PublishAck).PacketId == packetId.Value)));
		}
		
		[Fact]
		public void when_sending_publish_with_qos_higher_than_zero_and_without_packet_id_then_fails()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { MaximumQualityOfService = QualityOfService.ExactlyOnce };
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = Mock.Of<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var subscribedClientId = Guid.NewGuid().ToString();
			var sessions = new List<ClientSession> { new ClientSession { ClientId = subscribedClientId, Clean = false } };

			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>> ())).Returns (sessions.AsQueryable());

			var publish = new Publish (topic, QualityOfService.AtLeastOnce, retain: false, duplicated: false);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
				retainedRepository, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var ex = Assert.Throws<AggregateException> (() => flow.ExecuteAsync (clientId, publish, channel.Object).Wait());

			Assert.True (ex.InnerException is ProtocolException);
		}

		[Fact]
		public async Task when_sending_publish_and_subscriber_with_qos1_send_publish_ack_then_publish_is_not_re_transmitted()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { 
				MaximumQualityOfService = QualityOfService.ExactlyOnce,
				WaitingTimeoutSecs = 2
			};
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var subscribedClientId = Guid.NewGuid().ToString();
			var requestedQoS = QualityOfService.AtLeastOnce;
			var sessions = new List<ClientSession> { new ClientSession {
				ClientId = subscribedClientId,
				Clean = false,
				Subscriptions = new List<ClientSubscription> { 
					new ClientSubscription { ClientId = subscribedClientId, MaximumQualityOfService = requestedQoS, TopicFilter = topic }
				}
			}};

			var clientReceiver = new Subject<IPacket> ();
			var clientSender = new Subject<IPacket> ();
			var clientChannel = new Mock<IChannel<IPacket>> ();

			clientSender.OfType<Publish>().Subscribe (p => {
				clientReceiver.OnNext (new PublishAck (p.PacketId.Value));
			});

			clientChannel.Setup (c => c.Receiver).Returns (clientReceiver);
			clientChannel.Setup (c => c.Sender).Returns (clientSender);
			clientChannel.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (packet => clientSender.OnNext (packet))
				.Returns(Task.Delay(0));
			topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns ( sessions.AsQueryable());

			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId)))
				.Returns (clientChannel.Object);

			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var publish = new Publish (topic, QualityOfService.ExactlyOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			await flow.ExecuteAsync (clientId, publish, channel.Object);

			Thread.Sleep (2000);

			publishSenderFlow.Verify (s => s.SendPublishAsync (It.Is<string>(x => x == subscribedClientId), 
				It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload)),
				It.Is<IChannel<IPacket>>(c => c == clientChannel.Object), It.Is<PendingMessageStatus>(x => x == PendingMessageStatus.PendingToSend)), Times.Once);
			clientChannel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is Publish)), Times.Never);
		}

		[Fact]
		public async Task when_sending_publish_and_subscriber_with_qos2_send_publish_received_then_publish_is_not_re_transmitted()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = new ProtocolConfiguration { 
				MaximumQualityOfService = QualityOfService.ExactlyOnce,
				WaitingTimeoutSecs = 2
			};
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = new Mock<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var topic = "foo/bar";

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
				retainedRepository.Object, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var subscribedClientId = Guid.NewGuid().ToString();
			var requestedQoS = QualityOfService.ExactlyOnce;
			var sessions = new List<ClientSession> { new ClientSession {
				ClientId = subscribedClientId,
				Clean = false,
				Subscriptions = new List<ClientSubscription> { 
					new ClientSubscription { ClientId = subscribedClientId, MaximumQualityOfService = requestedQoS, TopicFilter = topic }
				}
			}};

			var clientReceiver = new Subject<IPacket> ();
			var clientSender = new Subject<IPacket> ();
			var clientChannel = new Mock<IChannel<IPacket>> ();

			clientSender.OfType<Publish>().Subscribe (p => {
				clientReceiver.OnNext (new PublishReceived (p.PacketId.Value));
			});

			clientChannel.Setup (c => c.Receiver).Returns (clientReceiver);
			clientChannel.Setup (c => c.Sender).Returns (clientSender);
			clientChannel.Setup (c => c.SendAsync (It.IsAny<IPacket> ()))
				.Callback<IPacket> (packet => clientSender.OnNext (packet))
				.Returns(Task.Delay(0));
			topicEvaluator.Setup (e => e.Matches (It.IsAny<string> (), It.IsAny<string> ())).Returns (true);
			sessionRepository.Setup (r => r.GetAll (It.IsAny<Expression<Func<ClientSession, bool>>>())).Returns ( sessions.AsQueryable());

			connectionProvider
				.Setup (p => p.GetConnection (It.Is<string> (s => s == subscribedClientId)))
				.Returns (clientChannel.Object);

			var packetId = (ushort?)new Random ().Next (0, ushort.MaxValue);
			var publish = new Publish (topic, QualityOfService.ExactlyOnce, retain: false, duplicated: false, packetId: packetId);

			publish.Payload = Encoding.UTF8.GetBytes ("Publish Receiver Flow Test");

			var receiver = new Subject<IPacket> ();
			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.Receiver).Returns (receiver);

			await flow.ExecuteAsync (clientId, publish, channel.Object);

			Thread.Sleep (2000);

			publishSenderFlow.Verify (s => s.SendPublishAsync (It.Is<string>(x => x == subscribedClientId), 
				It.Is<Publish> (p => p.Topic == publish.Topic &&
					p.Payload.ToList().SequenceEqual(publish.Payload)),
				It.Is<IChannel<IPacket>>(c => c == clientChannel.Object), It.Is<PendingMessageStatus>(x => x == PendingMessageStatus.PendingToSend)), Times.Once);
			clientChannel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is Publish)), Times.Never);
		}

		[Fact]
		public async Task when_sending_publish_release_then_publish_complete_is_sent()
		{
			var clientId = Guid.NewGuid ().ToString ();

			var configuration = Mock.Of<ProtocolConfiguration> ();
			var topicEvaluator = new Mock<ITopicEvaluator> ();
			var connectionProvider = new Mock<IConnectionProvider> ();
			var publishSenderFlow = new Mock<IPublishSenderFlow> ();
			var retainedRepository = Mock.Of<IRepository<RetainedMessage>> ();
			var sessionRepository = new Mock<IRepository<ClientSession>> ();

			sessionRepository.Setup (r => r.Get (It.IsAny<Expression<Func<ClientSession, bool>>> ()))
				.Returns (new ClientSession {
					ClientId = clientId,
					PendingMessages = new List<PendingMessage> { new PendingMessage() }
				});

			var packetIdProvider = Mock.Of<IPacketIdProvider> ();
			var eventStream = new EventStream ();

			var flow = new ServerPublishReceiverFlow (topicEvaluator.Object, connectionProvider.Object, publishSenderFlow.Object,
				retainedRepository, sessionRepository.Object, packetIdProvider, eventStream, configuration);

			var packetId = (ushort)new Random ().Next (0, ushort.MaxValue);
			var publishRelease = new PublishRelease (packetId);

			var channel = new Mock<IChannel<IPacket>> ();

			channel.Setup (c => c.IsConnected).Returns (true);

			await flow.ExecuteAsync (clientId, publishRelease, channel.Object);

			channel.Verify (c => c.SendAsync (It.Is<IPacket> (p => p is PublishComplete && (p as PublishComplete).PacketId == packetId)));
		}
	}
}
