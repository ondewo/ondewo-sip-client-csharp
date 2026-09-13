using System;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Ondewo.Sip;
using Xunit;

namespace Ondewo.Sip.Client.Tests
{
    /// <summary>
    /// The product-specific half of the suite: concrete assertions against the ONDEWO SIP API,
    /// spelled out with real message, field, enum and RPC names.
    /// <para>
    /// This is the only test file that has to be rewritten when the setup is replicated to another
    /// ONDEWO product - <see cref="GeneratedStubsTests"/> carries over unchanged. The SIP API has
    /// neither a streaming RPC nor a proto3 <c>optional</c> field, so it carries no case for
    /// either.
    /// </para>
    /// </summary>
    public class SipStubsTests
    {
        private const string DummyTarget = "http://localhost:50051";

        [Fact]
        public void SipStatusRoundTripsEveryScalarFieldKind()
        {
            var status = new SipStatus
            {
                AccountName = "sip-user-1@ondewo.com",
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000)),
                StatusType = SipStatus.Types.StatusType.OutgoingCallConnected,
                CalleeId = "sip-user-2@ondewo.com",
                TransferCallId = "sip-user-3@ondewo.com",
                Description = "call connected",
                ExceptionName = string.Empty,
                ExceptionTraceback = string.Empty,
                NluSessionName = "projects/1/agent/sessions/42",
            };

            byte[] bytes = status.ToByteArray();
            SipStatus parsed = SipStatus.Parser.ParseFrom(bytes);

            Assert.NotEmpty(bytes);
            Assert.Equal(status, parsed);
            Assert.Equal("sip-user-1@ondewo.com", parsed.AccountName);
            Assert.Equal(1_700_000_000L, parsed.Timestamp.Seconds);
            Assert.Equal(SipStatus.Types.StatusType.OutgoingCallConnected, parsed.StatusType);
            Assert.Equal("sip-user-2@ondewo.com", parsed.CalleeId);
            Assert.Equal("sip-user-3@ondewo.com", parsed.TransferCallId);
            Assert.Equal("call connected", parsed.Description);
            Assert.Equal("projects/1/agent/sessions/42", parsed.NluSessionName);
        }

        [Fact]
        public void MapFieldRoundTripsThroughAStartCallRequest()
        {
            var request = new SipStartCallRequest { CalleeId = "sip-user-2@ondewo.com" };
            request.Headers.Add("X-Ondewo-Tenant", "acme");
            request.Headers.Add("X-Ondewo-Session", "42");

            SipStartCallRequest parsed = SipStartCallRequest.Parser.ParseFrom(request.ToByteArray());

            Assert.Equal(request, parsed);
            Assert.Equal("sip-user-2@ondewo.com", parsed.CalleeId);
            Assert.Equal(2, parsed.Headers.Count);
            Assert.Equal("acme", parsed.Headers["X-Ondewo-Tenant"]);
            Assert.Equal("42", parsed.Headers["X-Ondewo-Session"]);
        }

        [Fact]
        public void RepeatedMessageFieldRoundTripsThroughTheStatusHistory()
        {
            var history = new SipStatusHistoryResponse();
            history.StatusHistory.Add(new SipStatus
            {
                AccountName = "sip-user-1@ondewo.com",
                StatusType = SipStatus.Types.StatusType.SessionStarted,
            });
            history.StatusHistory.Add(new SipStatus
            {
                AccountName = "sip-user-1@ondewo.com",
                StatusType = SipStatus.Types.StatusType.OutgoingCallInitiated,
            });

            SipStatusHistoryResponse parsed =
                SipStatusHistoryResponse.Parser.ParseFrom(history.ToByteArray());

            Assert.Equal(history, parsed);
            Assert.Equal(
                new[]
                {
                    SipStatus.Types.StatusType.SessionStarted,
                    SipStatus.Types.StatusType.OutgoingCallInitiated,
                },
                parsed.StatusHistory.Select(entry => entry.StatusType));
        }

        [Fact]
        public void RepeatedBytesFieldRoundTripsThroughAPlayWavFilesRequest()
        {
            var request = new SipPlayWavFilesRequest();
            request.WavFiles.Add(ByteString.CopyFromUtf8("RIFF-one"));
            request.WavFiles.Add(ByteString.CopyFromUtf8("RIFF-two"));

            SipPlayWavFilesRequest parsed =
                SipPlayWavFilesRequest.Parser.ParseFrom(request.ToByteArray());

            Assert.Equal(request, parsed);
            Assert.Equal(
                new[] { ByteString.CopyFromUtf8("RIFF-one"), ByteString.CopyFromUtf8("RIFF-two") },
                parsed.WavFiles);
        }

        [Fact]
        public void BooleanAndIntegerFieldsRoundTripTheirNonDefaultValues()
        {
            var endCall = new SipEndCallRequest { HardHangup = true };
            var startSession = new SipStartSessionRequest
            {
                AccountName = "sip-user-1@ondewo.com:5099",
                AutoAnswerInterval = 3,
            };

            Assert.True(SipEndCallRequest.Parser.ParseFrom(endCall.ToByteArray()).HardHangup);

            SipStartSessionRequest parsedSession =
                SipStartSessionRequest.Parser.ParseFrom(startSession.ToByteArray());

            Assert.Equal("sip-user-1@ondewo.com:5099", parsedSession.AccountName);
            Assert.Equal(3, parsedSession.AutoAnswerInterval);
        }

        [Fact]
        public void UnsetScalarFieldsCarryTheProto3DefaultsAndStayOffTheWire()
        {
            var status = new SipStatus();

            Assert.Equal(string.Empty, status.AccountName);
            Assert.Equal(SipStatus.Types.StatusType.NoSession, status.StatusType);
            Assert.Null(status.Timestamp);
            Assert.Empty(status.Headers);
            Assert.Empty(status.ToByteArray());

            // `hard_hangup` is a plain proto3 bool: false is indistinguishable from unset, which is
            // why the SIP API has no explicit-presence field anywhere.
            Assert.Empty(new SipEndCallRequest { HardHangup = false }.ToByteArray());
        }

        [Fact]
        public void EnumStartsAtItsZeroValue()
        {
            Assert.Equal(0, (int)SipStatus.Types.StatusType.NoSession);
            Assert.Equal(
                SipStatus.Types.StatusType.NoSession,
                default(SipStatus.Types.StatusType));

            // The C# name is PascalCased; the wire/JSON name is the one the server speaks.
            Assert.Equal(
                "NO_SESSION",
                SipStatus.Types.StatusType.NoSession.GetType()
                    .GetField(nameof(SipStatus.Types.StatusType.NoSession))
                    .GetCustomAttributes(typeof(Google.Protobuf.Reflection.OriginalNameAttribute), false)
                    .Cast<Google.Protobuf.Reflection.OriginalNameAttribute>()
                    .Single()
                    .Name);
        }

        [Fact]
        public void EnumFieldRoundTripsANonDefaultValue()
        {
            var status = new SipStatus { StatusType = SipStatus.Types.StatusType.MicrophoneMuted };

            SipStatus parsed = SipStatus.Parser.ParseFrom(status.ToByteArray());

            Assert.Equal(SipStatus.Types.StatusType.MicrophoneMuted, parsed.StatusType);
            Assert.NotEmpty(status.ToByteArray());
        }

        [Fact]
        public void SipClientBindsToAChannelAndExposesTheDeclaredRpcs()
        {
            using GrpcChannel channel = GrpcChannel.ForAddress(DummyTarget);

            var client = new Sip.SipClient(channel);

            Assert.NotNull(client);
            Assert.Equal("ondewo.sip.Sip", Sip.Descriptor.FullName);
            Assert.Contains(Sip.Descriptor.Methods, method => method.Name == "SipStartCall");

            string[] clientMethods = typeof(Sip.SipClient)
                .GetMethods()
                .Select(method => method.Name)
                .Distinct()
                .ToArray();

            foreach (string rpc in new[]
                     {
                         "SipStartSession", "SipEndSession", "SipStartCall", "SipEndCall",
                         "SipTransferCall", "SipRegisterAccount", "SipGetSipStatus",
                         "SipGetSipStatusHistory", "SipPlayWavFiles", "SipMute", "SipUnMute",
                     })
            {
                Assert.Contains(rpc, clientMethods);
                Assert.Contains(rpc + "Async", clientMethods);
            }
        }

        /// <summary>
        /// The SIP API declares no streaming RPC at all - every method is unary, which is what
        /// makes the unary/Async pair above the complete client surface.
        /// </summary>
        [Fact]
        public void EveryRpcIsUnary()
        {
            Assert.NotEmpty(Sip.Descriptor.Methods);
            Assert.All(Sip.Descriptor.Methods, method => Assert.False(method.IsClientStreaming));
            Assert.All(Sip.Descriptor.Methods, method => Assert.False(method.IsServerStreaming));
        }

        /// <summary>
        /// Six of the eleven RPCs take or return <c>google.protobuf.Empty</c>, which is supplied by
        /// the Google.Protobuf package rather than generated into this assembly.
        /// </summary>
        [Fact]
        public void WellKnownEmptyComesFromTheProtobufPackage()
        {
            Assert.Equal(
                "google.protobuf.Empty",
                Sip.Descriptor.FindMethodByName("SipGetSipStatus").InputType.FullName);
            Assert.NotSame(
                typeof(SipStatus).Assembly,
                typeof(Empty).Assembly);
        }
    }
}
