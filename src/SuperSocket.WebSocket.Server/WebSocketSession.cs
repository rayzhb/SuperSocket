using System;
using System.Buffers;
using System.Collections.Specialized;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SuperSocket.ProtoBase;
using SuperSocket.Server;
using SuperSocket.Server.Abstractions.Session;
using ConnectionCloseReason = SuperSocket.Connection.CloseReason;

namespace SuperSocket.WebSocket.Server
{
    public class WebSocketSession : AppSession, IHandshakeRequiredSession
    {
        public bool Handshaked { get; internal set; }

        public HttpHeader HttpHeader { get; internal set; }

        public string Path
        {
            get { return HttpHeader.Path; }
        }

        public string SubProtocol { get; internal set; }

        internal ISubProtocolHandler SubProtocolHandler { get; set; }

        public DateTime CloseHandshakeStartTime { get; private set; }

        public event EventHandler CloseHandshakeStarted;

        internal CloseStatus CloseStatus { get; set; }        

        internal IPackageEncoder<WebSocketPackage> MessageEncoder { get; set; }

        public virtual ValueTask SendAsync(WebSocketPackage message, CancellationToken cancellationToken = default)
        {
            return this.Connection.SendAsync(MessageEncoder, message, cancellationToken);
        }

        public virtual ValueTask SendAsync(string message, CancellationToken cancellationToken = default)
        {
            return SendAsync(new WebSocketPackage
                {
                    OpCode = OpCode.Text,
                    Message = message,
                },
                cancellationToken);
        }

        public virtual ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            return SendAsync(new WebSocketPackage
                {
                    OpCode = OpCode.Binary,
                    Data = new ReadOnlySequence<byte>(data),
                },
                cancellationToken);
        }

        public virtual ValueTask SendAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken = default)
        {
            return SendAsync(new WebSocketPackage
                {
                    OpCode = OpCode.Binary,
                    Data = data,
                },
                cancellationToken);
        }

        public ValueTask CloseAsync(CloseReason reason, string reasonText = null, CancellationToken cancellationToken = default)
        {
            var closeReasonCode = (short)reason;

            var closeStatus = new CloseStatus
            {
                Reason = reason
            };

            var textEncodedLen = 0;

            if (!string.IsNullOrEmpty(reasonText))
                textEncodedLen = Encoding.UTF8.GetMaxByteCount(reasonText.Length);

            var buffer = new byte[textEncodedLen + 2];

            buffer[0] = (byte) (closeReasonCode / 256);
            buffer[1] = (byte) (closeReasonCode % 256);

            var length = 2;

            if (!string.IsNullOrEmpty(reasonText))
            {
                closeStatus.ReasonText = reasonText;
                var span = new Span<byte>(buffer, 2, buffer.Length - 2);
                length += Encoding.UTF8.GetBytes(reasonText.AsSpan(), span);
            }

            CloseStatus = closeStatus;

            CloseHandshakeStartTime = DateTime.Now;
            OnCloseHandshakeStarted();

            return SendAsync(new WebSocketPackage
                {
                    OpCode = OpCode.Close,
                    Data = new ReadOnlySequence<byte>(buffer, 0, length)
                },
                cancellationToken);
        }

        private void OnCloseHandshakeStarted()
        {
            CloseHandshakeStarted?.Invoke(this, EventArgs.Empty);
        }

        internal void CloseWithoutHandshake()
        {
            base.CloseAsync(ConnectionCloseReason.LocalClosing).DoNotAwait();
        }

        public override async ValueTask CloseAsync(ConnectionCloseReason closeReason)
        {
            var closeStatus = CloseStatus;

            if (closeStatus != null)
            {
                var clientInitiated = closeStatus.RemoteInitiated;
                await base.CloseAsync(clientInitiated ? ConnectionCloseReason.RemoteClosing : ConnectionCloseReason.LocalClosing);
                return;
            }

            try
            {
                await CloseAsync(CloseReason.NormalClosure);
            }
            catch
            {

            }
        }

        public override async ValueTask CloseAsync()
        {
            await this.CloseAsync(CloseReason.NormalClosure);
        }
    }
}