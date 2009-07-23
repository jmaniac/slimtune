﻿/*
* Copyright (c) 2009 SlimDX Group
* All rights reserved. This program and the accompanying materials
* are made available under the terms of the Eclipse Public License v1.0
* which accompanies this distribution, and is available at
* http://www.eclipse.org/legal/epl-v10.html
*/
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

namespace SlimTuneUI
{
	public enum ProfilerMode
	{
		PM_Disabled = 0,

		PM_Sampling = 0x01,
		PM_Tracing = 0x02,

		PM_Hybrid = PM_Sampling | PM_Tracing,
	}

	class ThreadInfo
	{
		//public long ThreadId;
		//public string Name;
	}

	public class FunctionInfo
	{
		public int FunctionId;
		public bool IsNative;
		public string Name;
		public int Hits;

		public FunctionInfo()
		{
		}

		public FunctionInfo(int funcId, bool isNative, string name)
		{
			FunctionId = funcId;
			Name = name;
			IsNative = isNative;
			Hits = 0;
		}
	}

	class ProfilerClient : IDisposable
	{
		TcpClient m_client;
		NetworkStream m_stream;
		BinaryReader m_reader;
		BinaryWriter m_writer;
		Dictionary<int, FunctionInfo> m_functions = new Dictionary<int, FunctionInfo>();
		Dictionary<long, ThreadInfo> m_threads = new Dictionary<long, ThreadInfo>();

		IStorageEngine m_storage;

		public Dictionary<int, FunctionInfo> Functions
		{
			get { return m_functions; }
		}

		public ProfilerClient(string server, int port, IStorageEngine storage)
		{
			m_client = new TcpClient();
			m_client.Connect("localhost", port);
			m_stream = m_client.GetStream();
			m_reader = new BinaryReader(m_stream, Encoding.Unicode);
			m_writer = new BinaryWriter(m_stream, Encoding.Unicode);
			m_storage = storage;

			Debug.WriteLine("Successfully connected.");
		}

		public string Receive()
		{
			try
			{
				if(m_stream == null)
					return string.Empty;

				MessageId messageId = (MessageId) m_reader.ReadByte();
				switch(messageId)
				{
					case MessageId.MID_MapFunction:
						var mapFunc = Messages.MapFunction.Read(m_reader);
						FunctionInfo funcInfo = new FunctionInfo();
						funcInfo.FunctionId = mapFunc.FunctionId;
						funcInfo.Name = mapFunc.Name;
						funcInfo.IsNative = mapFunc.IsNative;
						m_storage.MapFunction(funcInfo);

						Debug.WriteLine(string.Format("Mapped {0} to {1}.", mapFunc.Name, mapFunc.FunctionId));
						break;

					case MessageId.MID_EnterFunction:
					case MessageId.MID_LeaveFunction:
					case MessageId.MID_TailCall:
						var funcEvent = Messages.FunctionEvent.Read(m_reader);
						if(!m_functions.ContainsKey(funcEvent.FunctionId))
							m_functions.Add(funcEvent.FunctionId, new FunctionInfo(funcEvent.FunctionId, false, "{Unknown}"));

						if(messageId == MessageId.MID_EnterFunction)
							m_functions[funcEvent.FunctionId].Hits++;

						break;

					case MessageId.MID_CreateThread:
					case MessageId.MID_DestroyThread:
						var threadEvent = Messages.CreateThread.Read(m_reader);
						m_storage.UpdateThread(threadEvent.ThreadId, messageId == MessageId.MID_CreateThread ? true : false, null);
						break;

					case MessageId.MID_NameThread:
						var nameThread = Messages.NameThread.Read(m_reader);
						//asume that dead threads can't be renamed
						m_storage.UpdateThread(nameThread.ThreadId, true, nameThread.Name);
						break;

					case MessageId.MID_Sample:
						var sample = Messages.Sample.Read(m_reader, m_functions);
						m_storage.ParseSample(sample);
						break;

					default:
						throw new InvalidOperationException();
				}

				return string.Empty;
			}
			catch(IOException)
			{
				return null;
			}
		}

		#region IDisposable Members

		public void Dispose()
		{
			m_stream.Dispose();
		}

		#endregion
	}
}
