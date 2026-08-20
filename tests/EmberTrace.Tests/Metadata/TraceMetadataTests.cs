using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EmberTrace.Metadata;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Metadata;

[TestClass]
[DoNotParallelize]
public class TraceMetadataTests
{
    private const int First = 910_001;
    private const int Second = 910_002;

    [TestMethod]
    public void CreateDefault_CachesUntilRegistrationChanges()
    {
        var provider = new EnumerableProvider((First, "First"));
        TraceMetadata.Register(provider);
        try
        {
            var snapshot = TraceMetadata.CreateDefault();
            Assert.AreSame(snapshot, TraceMetadata.CreateDefault(), "repeated resolves must reuse the flattened snapshot");

            var other = new EnumerableProvider((Second, "Second"));
            TraceMetadata.Register(other);
            try
            {
                Assert.AreNotSame(snapshot, TraceMetadata.CreateDefault(), "registering must invalidate the snapshot");
            }
            finally
            {
                TraceMetadata.Unregister(other);
            }
        }
        finally
        {
            TraceMetadata.Unregister(provider);
        }
    }

    [TestMethod]
    public void CreateDefault_FlattensEveryEnumerableProviderIntoOneLookup()
    {
        var a = new EnumerableProvider((First, "First"));
        var b = new EnumerableProvider((Second, "Second"));

        TraceMetadata.Register(a);
        TraceMetadata.Register(b);
        try
        {
            var meta = TraceMetadata.CreateDefault();

            Assert.IsTrue(meta.TryGet(First, out var first));
            Assert.AreEqual("First", first.Name);
            Assert.IsTrue(meta.TryGet(Second, out var second));
            Assert.AreEqual("Second", second.Name);
            Assert.AreEqual(0, a.Lookups + b.Lookups, "flattened entries must not fall back to the source providers");
        }
        finally
        {
            TraceMetadata.Unregister(a);
            TraceMetadata.Unregister(b);
        }
    }

    [TestMethod]
    public void CreateDefault_KeepsNonEnumerableProvidersAsFallback()
    {
        var opaque = new OpaqueProvider(First, "Opaque");
        TraceMetadata.Register(opaque);
        try
        {
            Assert.IsTrue(TraceMetadata.CreateDefault().TryGet(First, out var meta));
            Assert.AreEqual("Opaque", meta.Name);
        }
        finally
        {
            TraceMetadata.Unregister(opaque);
        }
    }

    [TestMethod]
    public void Unregister_RemovesTheProviderAndReportsWhetherItWasRegistered()
    {
        var provider = new EnumerableProvider((First, "First"));

        Assert.IsFalse(TraceMetadata.Unregister(provider));

        TraceMetadata.Register(provider);
        Assert.IsTrue(TraceMetadata.Unregister(provider));
        Assert.IsFalse(TraceMetadata.CreateDefault().TryGet(First, out _));
    }

    [TestMethod]
    public void Reset_DropsEveryRegistration()
    {
        TraceMetadata.Register(new EnumerableProvider((First, "First")));
        TraceMetadata.Reset();

        Assert.IsFalse(TraceMetadata.CreateDefault().TryGet(First, out _));
    }

    [TestMethod]
    public void Register_NullProvider_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceMetadata.Register(null!));
    }

    private sealed class EnumerableProvider : ITraceMetadataProvider, IEnumerable<TraceMeta>
    {
        private readonly TraceMeta[] _entries;

        public EnumerableProvider(params (int Id, string Name)[] entries)
        {
            _entries = Array.ConvertAll(entries, e => new TraceMeta(e.Id, e.Name, null));
        }

        public int Lookups { get; private set; }

        public bool TryGet(int id, out TraceMeta metadata)
        {
            Lookups++;
            foreach (var entry in _entries.Where(entry => entry.Id == id))
            {
                metadata = entry;
                return true;
            }

            metadata = default;
            return false;
        }

        public IEnumerator<TraceMeta> GetEnumerator() => ((IEnumerable<TraceMeta>)_entries).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class OpaqueProvider(int id, string name) : ITraceMetadataProvider
    {
        private readonly TraceMeta _meta = new(id, name, null);

        public bool TryGet(int lookupId, out TraceMeta metadata)
        {
            if (lookupId == id)
            {
                metadata = _meta;
                return true;
            }

            metadata = default;
            return false;
        }
    }
}
