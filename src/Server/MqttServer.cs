﻿using System.Net.Mqtt.Sdk;

namespace System.Net.Mqtt
{
	/// <summary>
	/// Creates instances of <see cref="IMqttServer"/> for accepting connections from 
	/// MQTT clients.
	/// </summary>
	public static class MqttServer
	{
		/// <summary>
		/// Creates an <see cref="IMqttServer"/> over the TCP protocol, using the 
		/// specified MQTT configuration to customize the protocol parameters.
		/// </summary>
		/// <param name="configuration">
		/// The configuration used for creating the Server.
		/// See <see cref="MqttConfiguration" /> for more details about the supported values.
		/// </param>
		/// <param name="authenticationProvider">
		/// Optional authentication provider to use, 
		/// to enable authentication as part of the connection mechanism.
		/// See <see cref="IMqttAuthenticationProvider" /> for more details about how to implement
		/// an authentication provider.
		/// </param>
		/// <returns>A new MQTT Server</returns>
		/// <exception cref="MqttServerException">MqttServerException</exception>
		public static IMqttServer Create(MqttConfiguration configuration, IMqttAuthenticationProvider authenticationProvider = null) =>
			new MqttServerFactory(authenticationProvider).CreateServer(configuration);

		/// <summary>
		/// Creates an <see cref="IMqttServer"/> over the TCP protocol, using the 
		/// specified port.
		/// </summary>
		/// <param name="port">
		/// The port to listen for incoming connections.
		/// </param>
		/// <returns>A new MQTT Server</returns>
		/// <exception cref="MqttServerException">MqttServerException</exception>
		public static IMqttServer Create(int port) => new MqttServerFactory().CreateServer(new MqttConfiguration { Port = port });

		/// <summary>
		/// Creates an <see cref="IMqttServer"/> over the TCP protocol, using the 
		/// MQTT protocol defaults.
		/// </summary>
		/// <returns>A new MQTT Server</returns>
		/// <exception cref="MqttServerException">MqttServerException</exception>
		public static IMqttServer Create() => new MqttServerFactory().CreateServer(new MqttConfiguration());
	}
}
