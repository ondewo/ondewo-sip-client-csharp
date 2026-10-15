using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Ondewo.Sip;
using Xunit;
using Type = System.Type;

namespace Ondewo.Sip.Client.Tests
{
    /// <summary>
    /// The product-specific half of the suite: concrete assertions against the ONDEWO SIP API,
    /// spelled out with real message, field, enum and RPC names.
    /// <para>
    /// This is the only test file that has to be rewritten when the setup is replicated to another
    /// ONDEWO product - <see cref="GeneratedStubsTests"/> carries over unchanged. The SIP API has
    /// no proto3 <c>optional</c> field, so it carries no case for one; its streaming RPC
    /// <c>SipStreamCallAudio</c> is pinned below.
    /// </para>
    /// <para>
    /// RPC names written here are MUST-CONTAIN pins - RPCs consumers rely on - and never the whole
    /// surface: the complete set of services and RPCs is DERIVED from the protos (or the committed
    /// stubs) by <see cref="SipApiSurface"/>, so the next API release adds RPCs without editing this file.
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
            Assert.Equal(SipApiSurface.RpcNamesOf("ondewo.sip.Sip"), Sip.Descriptor.Methods.Select(method => method.Name));

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
                         "SipReportAnsweringMachineDetected", "SipSetCallMediaControl",
                     })
            {
                Assert.Contains(rpc, clientMethods);
                Assert.Contains(rpc + "Async", clientMethods);
            }
        }

        /// <summary>
        /// <c>SipStreamCallAudio</c> (5.5.0) is bidirectional: the generated client exposes it as a
        /// single call returning an <see cref="AsyncDuplexStreamingCall{TRequest, TResponse}"/>, with no
        /// blocking and no <c>Async</c> variant. Which RPCs stream at all is derived from the protos
        /// (<see cref="EveryServiceTheSipApiDeclaresIsGeneratedWithEveryRpcAndItsStreamingShape"/>).
        /// </summary>
        [Fact]
        public void SipStreamCallAudioStreamsBothWays()
        {
            MethodDescriptor audio = Sip.Descriptor.FindMethodByName("SipStreamCallAudio");

            Assert.NotNull(audio);
            Assert.True(audio.IsClientStreaming);
            Assert.True(audio.IsServerStreaming);
            Assert.Equal("ondewo.sip.SipCallAudioRequest", audio.InputType.FullName);
            Assert.Equal("ondewo.sip.SipCallAudioResponse", audio.OutputType.FullName);

            var call = typeof(Sip.SipClient).GetMethods()
                .Where(method => method.Name == "SipStreamCallAudio")
                .ToList();
            Assert.NotEmpty(call);
            Assert.All(call, method => Assert.Equal(
                typeof(AsyncDuplexStreamingCall<SipCallAudioRequest, SipCallAudioResponse>),
                method.ReturnType));
            Assert.DoesNotContain(
                typeof(Sip.SipClient).GetMethods(),
                method => method.Name == "SipStreamCallAudioAsync");
        }

        /// <summary>
        /// SIP API 5.5.0 declares <c>idempotency_level = NO_SIDE_EFFECTS</c> on the two status reads
        /// (a must-contain pin: a later API may mark further reads) and not on <c>SipEndCall</c>; the
        /// option travels with the generated descriptor.
        /// </summary>
        [Fact]
        public void TheStatusReadsDeclareNoSideEffects()
        {
            string[] noSideEffects = Sip.Descriptor.Methods
                .Where(method => method.GetOptions()?.IdempotencyLevel
                                 == MethodOptions.Types.IdempotencyLevel.NoSideEffects)
                .Select(method => method.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Contains("SipGetSipStatus", noSideEffects);
            Assert.Contains("SipGetSipStatusHistory", noSideEffects);
            Assert.DoesNotContain("SipEndCall", noSideEffects);
            Assert.Equal(
                MethodOptions.Types.IdempotencyLevel.IdempotencyUnknown,
                Sip.Descriptor.FindMethodByName("SipEndCall").GetOptions()?.IdempotencyLevel
                    ?? MethodOptions.Types.IdempotencyLevel.IdempotencyUnknown);
        }

        [Fact]
        public void CallAudioRequestOneofRoundTripsConfigAndFrame()
        {
            var config = new SipCallAudioRequest
            {
                Config = new SipCallAudioConfig
                {
                    Mode = SipCallAudioMode.Talk,
                    SampleRateHz = 16000,
                    FrameMs = 20,
                    TakeOver = true,
                    StreamId = "operator-1",
                },
            };

            SipCallAudioRequest parsed = SipCallAudioRequest.Parser.ParseFrom(config.ToByteArray());

            Assert.Equal(SipCallAudioRequest.RequestOneofCase.Config, parsed.RequestCase);
            Assert.Equal(config, parsed);
            Assert.Equal(16000, parsed.Config.SampleRateHz);
            Assert.True(parsed.Config.TakeOver);

            var muted = new SipCallAudioRequest { AgentMuted = true };
            Assert.Equal(
                SipCallAudioRequest.RequestOneofCase.AgentMuted,
                SipCallAudioRequest.Parser.ParseFrom(muted.ToByteArray()).RequestCase);
        }

        [Fact]
        public void MediaControlAndAnsweringMachineFieldsRoundTrip()
        {
            var request = new SipSetCallMediaControlRequest
            {
                BotVoice = MediaControlSetting.Off,
                BotListening = MediaControlSetting.On,
                ParticipantsPresent = true,
            };
            Assert.Equal(request, SipSetCallMediaControlRequest.Parser.ParseFrom(request.ToByteArray()));

            var status = new SipStatus
            {
                StatusType = SipStatus.Types.StatusType.OutgoingCallAnsweringMachineDetected,
                CallId = "call-1",
                BotMuted = true,
                ListeningPaused = true,
                CallAudioStreams = 2,
                SipResponseCode = 486,
                AmdResult = new AnsweringMachineDetectionResult { CallId = "call-1" },
            };
            SipStatus parsed = SipStatus.Parser.ParseFrom(status.ToByteArray());

            Assert.Equal(status, parsed);
            Assert.Equal(22, (int)parsed.StatusType);
            Assert.Equal("call-1", parsed.AmdResult.CallId);

            Assert.Equal(3, (int)SipEndCallRequest.Types.EndCallReason.Transferred);
        }

        /// <summary>
        /// Several RPCs take or return <c>google.protobuf.Empty</c>, which is supplied by
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

        /// <summary>
        /// Every service the SIP protos declare is generated - nothing more, nothing less - with exactly
        /// the declared RPCs, in declaration order, each with its declared streaming shape, and a client
        /// that exposes it: a unary RPC as a blocking and an <c>...Async</c> method, a streaming RPC as one.
        /// Derived from the protos (or the committed stubs), so a new RPC needs no test edit.
        /// </summary>
        [Fact]
        public void EveryServiceTheSipApiDeclaresIsGeneratedWithEveryRpcAndItsStreamingShape()
        {
            using GrpcChannel channel = GrpcChannel.ForAddress(DummyTarget);
            Type[] serviceClasses = ProductStubs.Assembly.GetTypes()
                .Where(type => type.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.PropertyType == typeof(ServiceDescriptor))
                .Where(type => ServiceDescriptorOf(type).FullName.StartsWith(SipApiSurface.Package + ".", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(
                SipApiSurface.Services.Keys.OrderBy(name => name, StringComparer.Ordinal),
                serviceClasses.Select(type => ServiceDescriptorOf(type).FullName).OrderBy(name => name, StringComparer.Ordinal));

            foreach (Type serviceClass in serviceClasses)
            {
                ServiceDescriptor service = ServiceDescriptorOf(serviceClass);
                IReadOnlyList<SipApiSurface.Rpc> declared = SipApiSurface.RpcsOf(service.FullName);
                Assert.Equal(declared.Select(rpc => rpc.Name), service.Methods.Select(method => method.Name));

                Type clientClass = serviceClass.GetNestedType(serviceClass.Name + "Client");
                Assert.NotNull(clientClass);
                Assert.NotNull(Activator.CreateInstance(clientClass, channel));
                string[] clientMethods = clientClass.GetMethods().Select(method => method.Name).Distinct().ToArray();

                foreach (SipApiSurface.Rpc rpc in declared)
                {
                    MethodDescriptor method = service.FindMethodByName(rpc.Name);
                    Assert.Equal(rpc.ClientStreaming, method.IsClientStreaming);
                    Assert.Equal(rpc.ServerStreaming, method.IsServerStreaming);
                    MethodType expected = (rpc.ClientStreaming, rpc.ServerStreaming) switch
                    {
                        (false, false) => MethodType.Unary,
                        (false, true) => MethodType.ServerStreaming,
                        (true, false) => MethodType.ClientStreaming,
                        _ => MethodType.DuplexStreaming,
                    };
                    Assert.Equal(expected, MethodTypeOf(serviceClass, rpc.Name));
                    Assert.Contains(rpc.Name, clientMethods);
                    if (expected == MethodType.Unary)
                    {
                        Assert.Contains(rpc.Name + "Async", clientMethods);
                    }
                    else
                    {
                        Assert.DoesNotContain(rpc.Name + "Async", clientMethods);
                    }
                }
            }
        }

        /// <summary>
        /// The derivation itself is pinned, so the test above cannot pass by deriving nothing: both
        /// parsers read real declarations, the product's service is found, and CI - which checks the
        /// api submodule out - really reads the protos rather than falling back.
        /// </summary>
        [Fact]
        public void TheDerivedApiSurfaceReadsRealDeclarations()
        {
            Dictionary<string, List<SipApiSurface.Rpc>> proto = SipApiSurface.ParseProto(
                "syntax = \"proto3\";\npackage ondewo.sip;\n// rpc Commented (A) returns (B);\n" +
                "service S {\n  rpc One (A) returns (B) { option (x) = { get: \"//v1/{name}\" }; }\n" +
                "  rpc Two(stream A)\n    returns (stream B);\n  rpc Three (A) returns (stream B);\n}\n");
            Assert.Equal(
                new[] { new SipApiSurface.Rpc("One", false, false), new SipApiSurface.Rpc("Two", true, true), new SipApiSurface.Rpc("Three", false, true) },
                Assert.Single(proto, entry => entry.Key == "ondewo.sip.S").Value);

            Dictionary<string, List<SipApiSurface.Rpc>> stub = SipApiSurface.ParseStub(
                File.ReadAllText(Path.Combine(SipApiSurface.RepositoryRoot, "api", "Ondewo", "Sip", "SipGrpc.cs")));
            Assert.Contains(new SipApiSurface.Rpc("SipStreamCallAudio", true, true), stub["ondewo.sip.Sip"]);
            Assert.Contains(new SipApiSurface.Rpc("SipStartCall", false, false), stub["ondewo.sip.Sip"]);

            Assert.Contains("ondewo.sip.Sip", SipApiSurface.Services.Keys);
            Assert.Contains(new SipApiSurface.Rpc("SipStreamCallAudio", true, true), SipApiSurface.RpcsOf("ondewo.sip.Sip"));

            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                Assert.Equal("protos", SipApiSurface.Source);
            }
        }

        private static ServiceDescriptor ServiceDescriptorOf(Type serviceClass) =>
            (ServiceDescriptor)serviceClass.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

        private static MethodType MethodTypeOf(Type serviceClass, string rpc)
        {
            FieldInfo field = serviceClass.GetField(
                "__Method_" + rpc, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);

            return Assert.IsAssignableFrom<IMethod>(field.GetValue(null)).Type;
        }
    }
}
