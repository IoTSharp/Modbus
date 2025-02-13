﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AMWD.Modbus.SerialOverTCP.Util
{
	internal static class Extensions
	{
		#region Enums

		public static T GetAttribute<T>(this Enum enumValue)
			where T : Attribute
		{
			if (enumValue != null)
			{
				var fi = enumValue.GetType().GetField(enumValue.ToString());
				var attrs = (T[])fi?.GetCustomAttributes(typeof(T), inherit: false);
				return attrs?.FirstOrDefault();
			}
			return default;
		}

		public static string GetDescription(this Enum enumValue)
			=> enumValue.GetAttribute<DescriptionAttribute>()?.Description ?? enumValue.ToString();

		#endregion Enums

		#region Task handling

		public static async void Forget(this Task task)
		{
			try
			{
				await task;
			}
			catch
			{ /* Task forgotten, so keep everything quiet. */ }
		}

		#endregion Task handling

		#region Stream
		public static async Task<byte> ReadExpectedByte(this Stream stream, CancellationToken cancellationToken = default)
		{
			return ( await ReadExpectedBytes(stream, 1, cancellationToken)).First();
		}
			public static async Task<byte[]> ReadExpectedBytes(this Stream stream, int expectedBytes, CancellationToken cancellationToken = default)
		{
			byte[] buffer = new byte[expectedBytes];
			int offset = 0;
			do
			{
				int count = await stream.ReadAsync(buffer, offset, expectedBytes - offset, cancellationToken);
				if (count < 1)
					throw new EndOfStreamException($"Expected to read {buffer.Length - offset} more bytes, but end of stream is reached");

				offset += count;
			}
			while (expectedBytes - offset > 0 && !cancellationToken.IsCancellationRequested);

			cancellationToken.ThrowIfCancellationRequested();
			return buffer;
		}

		#endregion Stream

		#region Exception

		public static string GetMessage(this Exception exception)
			=> exception.InnerException?.Message ?? exception.Message;

		#endregion Exception
	}
}
