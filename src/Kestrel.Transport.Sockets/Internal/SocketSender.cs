﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketSender : SocketOperation
    {
        private static unsafe readonly IOCompletionCallback _completionCallback = new IOCompletionCallback(CompletionCallback);

        private SocketConnection _socketConnection;
        private ThreadPoolBoundHandle _threadPoolBoundHandle;

        private MemoryHandle _memoryHandle;
        private MultiSegmentSocketSender _multiSegmentSocketSender;

        internal SocketSender(Socket socket, SocketConnection socketConnection, ThreadPoolBoundHandle threadPoolBoundHandle)
            : base(socket, socketConnection, threadPoolBoundHandle, _completionCallback)
        {
            _socketConnection = socketConnection;
            _threadPoolBoundHandle = threadPoolBoundHandle;
        }

        public unsafe SocketAwaitable SendAsync(ReadOnlySequence<byte> buffers)
        {
            if (!buffers.IsSingleSegment)
            {
                if (_multiSegmentSocketSender == null)
                {
                    _multiSegmentSocketSender = new MultiSegmentSocketSender(_socket, _socketConnection, _threadPoolBoundHandle);
                }

                return _multiSegmentSocketSender.SendAsync(buffers);
            }

            var overlapped = GetOverlapped();

            _memoryHandle = buffers.First.Retain(pin: true);
            
            var wsaBuffer = new WSABuffer
            {
                Length = buffers.First.Length,
                Pointer = (IntPtr)_memoryHandle.Pointer
            };

            var errno = WSASend(
                _socket.Handle,
                &wsaBuffer,
                1,
                out var bytesTransferred,
                SocketFlags.None,
                overlapped,
                IntPtr.Zero);

            var awaitable = GetAwaitable(overlapped, errno, bytesTransferred, out var completedInline);

            if (completedInline)
            {
                _memoryHandle.Dispose();
            }

            return awaitable;
        }

        private static unsafe void CompletionCallback(uint errno, uint bytesTransferred, NativeOverlapped* overlapped)
        {
            var socketSender = (SocketSender)ThreadPoolBoundHandle.GetNativeOverlappedState(overlapped);
            socketSender._memoryHandle.Dispose();
            socketSender.OperationCompletionCallback(errno, bytesTransferred, overlapped);
        }
    }
}
