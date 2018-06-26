﻿using AMWD.Modbus.Common;
using AMWD.Modbus.Common.Interfaces;
using AMWD.Modbus.Common.Structures;
using AMWD.Modbus.Common.Util;
using AMWD.Modbus.Tcp.Protocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AMWD.Modbus.Tcp.Client
{
	/// <summary>
	/// A client to communicate with modbus devices via TCP.
	/// </summary>
	public class ModbusClient : IModbusClient
	{
		#region Fields

		private readonly object reconnectLock = new object();
		private readonly object sendLock = new object();
		private TcpClient tcpClient;
		private bool reconnectFailed = false;
		private bool wasConnected = false;

		private int sendTimeout = 1000;
		private int receiveTimeout = 1000;

		#endregion Fields

		#region Events

		/// <summary>
		/// Raised when the client has the connection successfully established.
		/// </summary>
		public event EventHandler Connected;

		/// <summary>
		/// Raised when the client has closed the connection.
		/// </summary>
		public event EventHandler Disconnected;

		#endregion Events

		#region Constructors

		/// <summary>
		/// Initializes a new instance of the <see cref="ModbusClient"/> class.
		/// </summary>
		/// <param name="host">The remote host name or ip.</param>
		/// <param name="port">The remote port.</param>
		public ModbusClient(string host, int port = 502)
		{
			if (string.IsNullOrWhiteSpace(host))
			{
				throw new ArgumentNullException(nameof(host));
			}
			if (port < 1 || port > 65535)
			{
				throw new ArgumentOutOfRangeException(nameof(port));
			}

			Host = host;
			Port = port;

			Connect();
		}

		#endregion Constructors

		#region Properties

		/// <summary>
		/// Gets or sets the host name.
		/// </summary>
		public string Host { get; private set; }

		/// <summary>
		/// Gets or sets the port.
		/// </summary>
		public int Port { get; private set; }

		/// <summary>
		/// Gets a value indicating whether the connection is established.
		/// </summary>
		public bool IsConnected => tcpClient?.Connected ?? false;

		/// <summary>
		/// Gets or sets the max. reconnect timespan until the reconnect is aborted.
		/// </summary>
		public TimeSpan ReconnectTimeSpan { get; set; } = TimeSpan.MaxValue;

		/// <summary>
		/// Gets or sets the send timeout in milliseconds. Default: 1000.
		/// </summary>
		public int SendTimeout
		{
			get
			{
				return sendTimeout;
			}
			set
			{
				sendTimeout = value;
				if (tcpClient != null)
				{
					tcpClient.SendTimeout = value;
				}
			}
		}

		/// <summary>
		/// Gets ors sets the receive timeout in milliseconds. Default: 1000;
		/// </summary>
		public int ReceiveTimeout
		{
			get
			{
				return receiveTimeout;
			}
			set
			{
				receiveTimeout = value;
				if (tcpClient != null)
				{
					tcpClient.ReceiveTimeout = value;
				}
			}
		}

		#endregion Properties

		#region Public methods

		#region Read methods

		/// <summary>
		/// Reads one or more coils of a device. (Modbus function 1).
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="startAddress">The first coil number to read.</param>
		/// <param name="count">The number of coils to read.</param>
		/// <returns>A list of coils or null on error.</returns>
		public async Task<List<Coil>> ReadCoils(byte deviceId, ushort startAddress, ushort count)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}
			if (startAddress < Consts.MinAddress || Consts.MaxAddress < startAddress + count)
			{
				throw new ArgumentOutOfRangeException(nameof(startAddress));
			}
			if (count < Consts.MinCount || Consts.MaxCoilCountRead < count)
			{
				throw new ArgumentOutOfRangeException(nameof(count));
			}

			List<Coil> list = null;
			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.ReadCoils,
					Address = startAddress,
					Count = count
				};
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new SocketException((int)SocketError.TimedOut);
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				if (request.TransactionId == response.TransactionId)
				{
					list = new List<Coil>();
					for (int i = 0; i < count; i++)
					{
						var posByte = i / 8;
						var posBit = i % 8;

						var val = response.Data[posByte] & (byte)Math.Pow(2, posBit);

						list.Add(new Coil
						{
							Address = (ushort)(startAddress + i),
							Value = val > 0
						});
					}
				}
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return list;
		}

		/// <summary>
		/// Reads one or more discrete inputs of a device. (Modbus function 2).
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="startAddress">The first discrete input number to read.</param>
		/// <param name="count">The number of discrete inputs to read.</param>
		/// <returns>A list of discrete inputs or null on error.</returns>
		public async Task<List<DiscreteInput>> ReadDiscreteInputs(byte deviceId, ushort startAddress, ushort count)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}
			if (startAddress < Consts.MinAddress || Consts.MaxAddress < startAddress + count)
			{
				throw new ArgumentOutOfRangeException(nameof(startAddress));
			}
			if (count < Consts.MinCount || Consts.MaxCoilCountRead < count)
			{
				throw new ArgumentOutOfRangeException(nameof(count));
			}

			List<DiscreteInput> list = null;
			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.ReadDiscreteInputs,
					Address = startAddress,
					Count = count
				};
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new SocketException((int)SocketError.TimedOut);
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				if (request.TransactionId == response.TransactionId)
				{
					list = new List<DiscreteInput>();
					for (int i = 0; i < count; i++)
					{
						var posByte = i / 8;
						var posBit = i % 8;

						var val = response.Data[posByte] & (byte)Math.Pow(2, posBit);

						list.Add(new DiscreteInput
						{
							Address = (ushort)(startAddress + i),
							Value = val > 0
						});
					}
				}
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return list;
		}

		/// <summary>
		/// Reads one or more holding registers of a device. (Modbus function 3).
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="startAddress">The first register number to read.</param>
		/// <param name="count">The number of registers to read.</param>
		/// <returns>A list of registers or null on error.</returns>
		public async Task<List<Register>> ReadHoldingRegisters(byte deviceId, ushort startAddress, ushort count)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}
			if (startAddress < Consts.MinAddress || Consts.MaxAddress < startAddress + count)
			{
				throw new ArgumentOutOfRangeException(nameof(startAddress));
			}
			if (count < Consts.MinCount || Consts.MaxRegisterCountRead < count)
			{
				throw new ArgumentOutOfRangeException(nameof(count));
			}

			List<Register> list = null;
			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.ReadHoldingRegisters,
					Address = startAddress,
					Count = count
				};
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new SocketException((int)SocketError.TimedOut);
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				if (request.TransactionId == response.TransactionId)
				{
					list = new List<Register>();
					for (int i = 0; i < count; i++)
					{
						list.Add(new Register
						{
							Address = (ushort)(startAddress + i),
							HiByte = response.Data[i * 2],
							LoByte = response.Data[i * 2 + 1]
						});
					}
				}
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return list;
		}

		/// <summary>
		/// Reads one or more input registers of a device. (Modbus function 4).
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="startAddress">The first register number to read.</param>
		/// <param name="count">The number of registers to read.</param>
		/// <returns>A list of registers or null on error.</returns>
		public async Task<List<Register>> ReadInputRegisters(byte deviceId, ushort startAddress, ushort count)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}
			if (startAddress < Consts.MinAddress || Consts.MaxAddress < startAddress + count)
			{
				throw new ArgumentOutOfRangeException(nameof(startAddress));
			}
			if (count < Consts.MinCount || Consts.MaxRegisterCountRead < count)
			{
				throw new ArgumentOutOfRangeException(nameof(count));
			}

			List<Register> list = null;
			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.ReadInputRegisters,
					Address = startAddress,
					Count = count
				};
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new SocketException((int)SocketError.TimedOut);
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				if (request.TransactionId == response.TransactionId)
				{
					list = new List<Register>();
					for (int i = 0; i < count; i++)
					{
						list.Add(new Register
						{
							Address = (ushort)(startAddress + i),
							HiByte = response.Data[i * 2],
							LoByte = response.Data[i * 2 + 1]
						});
					}
				}
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return list;
		}

		#endregion Read methods

		#region Write methods

		/// <summary>
		/// Writes a single coil status to the Modbus device. (Modbus function 5)
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="coil">The coil to write.</param>
		/// <returns>true on success, otherwise false.</returns>
		public async Task<bool> WriteSingleCoil(byte deviceId, Coil coil)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (coil == null)
			{
				throw new ArgumentNullException(nameof(coil));
			}
			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}
			if (coil.Address < Consts.MinAddress || Consts.MaxAddress < coil.Address)
			{
				throw new ArgumentOutOfRangeException(nameof(coil.Address));
			}

			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.WriteSingleCoil,
					Address = coil.Address,
					Data = new DataBuffer(2)
				};
				var value = (ushort)(coil.Value ? 0xFF00 : 0x0000);
				request.Data.SetUInt16(0, value);
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new ModbusException("Response timed out. Device id invalid?");
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				return request.TransactionId == response.TransactionId &&
					request.DeviceId == response.DeviceId &&
					request.Function == response.Function &&
					request.Address == response.Address &&
					request.Data.Equals(response.Data);
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return false;
		}

		/// <summary>
		/// Writes a single register to the Modbus device. (Modbus function 6)
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="register">The register to write.</param>
		/// <returns>true on success, otherwise false.</returns>
		public async Task<bool> WriteSingleRegister(byte deviceId, Register register)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (register == null)
			{
				throw new ArgumentNullException(nameof(register));
			}
			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}
			if (register.Address < Consts.MinAddress || Consts.MaxAddress < register.Address)
			{
				throw new ArgumentOutOfRangeException(nameof(register.Address));
			}

			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.WriteSingleRegister,
					Address = register.Address,
					Data = new DataBuffer(new[] { register.HiByte, register.LoByte })
				};
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new ModbusException("Response timed out. Device id invalid?");
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				return request.TransactionId == response.TransactionId &&
					request.DeviceId == response.DeviceId &&
					request.Function == response.Function &&
					request.Address == response.Address &&
					request.Data.Equals(response.Data);
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return false;
		}

		/// <summary>
		/// Writes multiple coil status to the Modbus device. (Modbus function 15)
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="coils">A list of coils to write.</param>
		/// <returns>true on success, otherwise false.</returns>
		public async Task<bool> WriteCoils(byte deviceId, IEnumerable<Coil> coils)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (coils == null || !coils.Any())
			{
				throw new ArgumentNullException(nameof(coils));
			}
			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}

			var orderedList = coils.OrderBy(c => c.Address).ToList();
			if (orderedList.Count < Consts.MinCount || Consts.MaxCoilCountWrite < orderedList.Count)
			{
				throw new ArgumentOutOfRangeException("Count");
			}

			var firstAddress = orderedList.First().Address;
			var lastAddress = orderedList.Last().Address;

			if (firstAddress + orderedList.Count - 1 != lastAddress)
			{
				throw new ArgumentException("No address gabs allowed within a request");
			}
			if (firstAddress < Consts.MinAddress || Consts.MaxAddress < lastAddress)
			{
				throw new ArgumentOutOfRangeException("Address");
			}

			var numBytes = (int)Math.Ceiling(orderedList.Count / 8.0);
			var coilBytes = new byte[numBytes];
			for (int i = 0; i < orderedList.Count; i++)
			{
				if (orderedList[i].Value)
				{
					var posByte = i / 8;
					var posBit = i % 8;

					var mask = (byte)Math.Pow(2, posBit);
					coilBytes[posByte] = (byte)(coilBytes[posByte] | mask);
				}
			}

			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.WriteMultipleCoils,
					Address = firstAddress,
					Count = (ushort)orderedList.Count,
					Data = new DataBuffer(coilBytes)
				};
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new ModbusException("Response timed out. Device id invalid?");
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				return request.TransactionId == response.TransactionId &&
					request.Address == response.Address &&
					request.Count == response.Count;
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return false;
		}

		/// <summary>
		/// Writes multiple registers to the Modbus device. (Modbus function 16)
		/// </summary>
		/// <param name="deviceId">The id to address the device (slave).</param>
		/// <param name="registers">A list of registers to write.</param>
		/// <returns>true on success, otherwise false.</returns>
		public async Task<bool> WriteRegisters(byte deviceId, IEnumerable<Register> registers)
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			if (registers == null || !registers.Any())
			{
				throw new ArgumentNullException(nameof(registers));
			}
			if (deviceId < Consts.MinDeviceId || Consts.MaxDeviceId < deviceId)
			{
				throw new ArgumentOutOfRangeException(nameof(deviceId));
			}

			var orderedList = registers.OrderBy(c => c.Address).ToList();
			if (orderedList.Count < Consts.MinCount || Consts.MaxRegisterCountWrite < orderedList.Count)
			{
				throw new ArgumentOutOfRangeException("Count");
			}

			var firstAddress = orderedList.First().Address;
			var lastAddress = orderedList.Last().Address;

			if (firstAddress + orderedList.Count - 1 != lastAddress)
			{
				throw new ArgumentException("No address gabs allowed within a request");
			}
			if (firstAddress < Consts.MinAddress || Consts.MaxAddress < lastAddress)
			{
				throw new ArgumentOutOfRangeException("Address");
			}

			try
			{
				var request = new Request
				{
					DeviceId = deviceId,
					Function = FunctionCode.WriteMultipleRegisters,
					Address = firstAddress,
					Count = (ushort)orderedList.Count,
					Data = new DataBuffer(orderedList.Count * 2 + 1)
				};

				request.Data.SetByte(0, (byte)(orderedList.Count * 2));
				for (int i = 0; i < orderedList.Count; i++)
				{
					request.Data.SetUInt16(i * 2 + 1, orderedList[i].Value);
				}
				var response = await SendRequest(request);
				if (response.IsTimeout)
				{
					throw new ModbusException("Response timed out. Device id invalid?");
				}
				if (response.IsError)
				{
					throw new ModbusException(response.ErrorMessage);
				}

				return request.TransactionId == response.TransactionId &&
					request.Address == response.Address &&
					request.Count == response.Count;
			}
			catch (SocketException)
			{
				Task.Run((Action)Reconnect).Forget();
			}
			catch (IOException)
			{
				Task.Run((Action)Reconnect).Forget();
			}

			return false;
		}

		#endregion Write methods

		#endregion Public methods

		#region Private methods

		private async void Connect()
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			await Task.Run((Action)Reconnect);
		}

		private void Reconnect()
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			lock (reconnectLock)
			{
				if (reconnectFailed)
				{
					throw new InvalidOperationException("Reconnecting has failed");
				}
				if (tcpClient?.Connected == true)
				{
					return;
				}

				if (wasConnected)
				{
					Task.Run(() => Disconnected?.Invoke(this, EventArgs.Empty));
				}

				var timeout = 4;
				var maxTimeout = 20;
				var startTime = DateTime.UtcNow;

				while (true)
				{
					try
					{
						tcpClient = new TcpClient(AddressFamily.InterNetworkV6);
						tcpClient.Client.DualMode = true;
						var result = tcpClient.BeginConnect(Host, Port, null, null);
						var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeout));
						timeout += 2;
						if (timeout > maxTimeout)
						{
							timeout = maxTimeout;
						}
						if (!success)
						{
							throw new SocketException((int)SocketError.TimedOut);
						}
						tcpClient.EndConnect(result);

						tcpClient.SendTimeout = SendTimeout;
						tcpClient.ReceiveTimeout = ReceiveTimeout;
					}
					catch (SocketException) when (ReconnectTimeSpan == TimeSpan.MaxValue || DateTime.UtcNow <= startTime + ReconnectTimeSpan)
					{
						Thread.Sleep(1000);
						continue;
					}
					catch (Exception ex)
					{
						reconnectFailed = true;
						if (isDisposed)
						{
							return;
						}

						if (wasConnected)
						{
							throw new IOException("Server connection lost, reconnect failed.", ex);
						}
						else
						{
							throw new IOException("Could not connect to the server.", ex);
						}
					}

					if (!wasConnected)
					{
						wasConnected = true;
					}

					Task.Run(() => Connected?.Invoke(this, EventArgs.Empty));
					break;
				}
			}
		}

		private async Task<Response> SendRequest(Request request)
		{
			if (!IsConnected)
			{
				throw new InvalidOperationException("No connection");
			}

			var stream = tcpClient.GetStream();
			var bytes = request.Serialize();
			await stream.WriteAsync(bytes, 0, bytes.Length);

			var responseBytes = new List<byte>();

			var buffer = new byte[6];
			var count = await stream.ReadAsync(buffer, 0, buffer.Length);
			responseBytes.AddRange(buffer.Take(count));

			bytes = buffer.Skip(4).Take(2).ToArray();
			if (BitConverter.IsLittleEndian)
			{
				Array.Reverse(bytes);
			}
			int following = BitConverter.ToUInt16(bytes, 0);

			do
			{
				buffer = new byte[following];
				count = await stream.ReadAsync(buffer, 0, buffer.Length);
				following -= count;
				responseBytes.AddRange(buffer.Take(count));
			}
			while (following > 0);

			return new Response(responseBytes.ToArray());
		}

		#endregion Private methods

		#region IDisposable implementation

		/// <summary>
		/// Releases all managed and unmanaged resources used by the <see cref="ModbusClient"/>.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		private bool isDisposed;

		private void Dispose(bool disposing)
		{
			if (disposing)
			{
				tcpClient?.Dispose();
				tcpClient = null;
			}

			isDisposed = true;
		}

		#endregion IDisposable implementation

		#region Overrides

		/// <inheritdoc/>
		public override string ToString()
		{
			if (isDisposed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}

			return $"Modbus TCP {Host}:{Port} - Connected: {IsConnected}";
		}

		#endregion Overrides
	}
}