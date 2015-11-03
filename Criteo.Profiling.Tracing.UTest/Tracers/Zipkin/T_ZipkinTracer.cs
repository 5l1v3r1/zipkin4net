﻿using System;
using Criteo.Profiling.Tracing.Annotation;
using Criteo.Profiling.Tracing.Tracers.Zipkin;
using Moq;
using NUnit.Framework;

namespace Criteo.Profiling.Tracing.UTest.Tracers.Zipkin
{
    [TestFixture]
    class T_ZipkinTracer
    {

        [SetUp]
        public void EnableAndClearTracers()
        {
            Trace.TracingEnabled = true;
            Trace.SamplingRate = 1f;
            Tracer.Clear();
        }

        [Test]
        [ExpectedException(typeof(ArgumentNullException))]
        public void ShouldThrowWithNullSender()
        {
            var tracer = new ZipkinTracer(null);
        }

        [Test]
        public void SpansAreLoggedAfterEndAnnotationClientRecv()
        {
            var mockedSender = new Mock<IZipkinSender>();
            var zipkinTracer = new ZipkinTracer(mockedSender.Object);

            var trace = Trace.CreateIfSampled();

            Record(zipkinTracer, trace, Annotations.ClientSend());
            Record(zipkinTracer, trace, Annotations.ClientRecv());

            mockedSender.Verify(sender => sender.Send(It.IsAny<byte[]>()), Times.Once());
        }

        [Test]
        public void SpansAreLoggedAfterEndAnnotationServerSend()
        {
            var mockedSender = new Mock<IZipkinSender>();
            var zipkinTracer = new ZipkinTracer(mockedSender.Object);

            var trace = Trace.CreateIfSampled();

            Record(zipkinTracer, trace, Annotations.ServerRecv());
            Record(zipkinTracer, trace, Annotations.ServerSend());

            mockedSender.Verify(sender => sender.Send(It.IsAny<byte[]>()), Times.Once());
        }

        [Test]
        public void RecordsShouldBeFlushedAfterTtl()
        {
            var mockedSender = new Mock<IZipkinSender>();
            var zipkinTracer = new ZipkinTracer(mockedSender.Object);

            var now = DateTime.UtcNow;

            var firstSpanId = new SpanId(traceId: 1, parentSpanId: 0, id: 4874542152, flags: Flags.Empty());
            var record = new Record(firstSpanId, now, Annotations.ServerRecv());

            zipkinTracer.Record(record);

            // futureTime = now + (ttl - 4)
            var futureTime = now.AddSeconds(ZipkinTracer.TimeToLive - 4); // of course test will fail if TTL is set lower than 4 seconds

            zipkinTracer.FlushOldSpans(futureTime); // shouldn't do anything since we haven't reached span ttl yet

            mockedSender.Verify(sender => sender.Send(It.IsAny<byte[]>()), Times.Never());

            var newerSpanId = new SpanId(traceId: 2, parentSpanId: 0, id: 9988415021, flags: Flags.Empty());
            var newerRecord = new Record(newerSpanId, futureTime, Annotations.ServerRecv());
            zipkinTracer.Record(newerRecord); // creates a second span

            futureTime = futureTime.AddSeconds(5); // = now + (ttl - 4) + 5 = now + ttl + 1

            zipkinTracer.FlushOldSpans(futureTime); // should flush only the first span since we are 1 second past its TTL but 5 seconds before the second span TTL

            mockedSender.Verify(sender => sender.Send(It.IsAny<byte[]>()), Times.Once());

            // "ServerSend" should make the second span "complete" hence the second span should be sent immediately
            var newerComplementaryRecord = new Record(newerSpanId, futureTime, Annotations.ServerSend());
            zipkinTracer.Record(newerComplementaryRecord);

            mockedSender.Verify(sender => sender.Send(It.IsAny<byte[]>()), Times.Exactly(2));
        }

        private static void Record(ITracer tracer, Trace trace, IAnnotation annotation)
        {
            var recordClientSend = new Record(trace.CurrentId, DateTime.UtcNow, annotation);
            tracer.Record(recordClientSend);
        }

    }
}
