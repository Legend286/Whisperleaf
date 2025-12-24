using System;
using System.Threading;
using Veldrid;

namespace Whisperleaf.AssetPipeline
{
    public class RefCountedTexture : IDisposable
    {
        public Texture DeviceTexture { get; }
        public string Name { get; }
        private int _refCount = 1;
        private readonly Action _onZeroRefs;

        public RefCountedTexture(Texture texture, string name, Action onZeroRefs)
        {
            DeviceTexture = texture;
            Name = name;
            _onZeroRefs = onZeroRefs;
        }

        public void AddRef()
        {
            Interlocked.Increment(ref _refCount);
        }

        public void Dispose()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                DeviceTexture.Dispose();
                _onZeroRefs?.Invoke();
                // Console.WriteLine($"[RefCount] Disposed texture: {Name}");
            }
        }
        
        public int RefCount => _refCount;
    }
}
