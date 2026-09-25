using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Memory;

/// <summary>
/// The <see cref="ISessionStorage"/>: binds a value's <see cref="IBindingHandle"/> to the address
/// reserved for it through an <see cref="ISessionMemoryAllocator"/>.
/// </summary>
/// <param name="allocator">The session's memory allocator; reserves address space only, never values.</param>
internal sealed class SessionStorage(ISessionMemoryAllocator allocator) : ISessionStorage
{
    private readonly Dictionary<MemoryAddress, IBindingHandle> _handleByAddress = [];
    private readonly Dictionary<MemoryAddress, byte[]> _bytesByAddress = [];

    // A value whose declared Size is 0 (Nothing, Null, Empty, an uninitialized array, a UDT with no
    // resolvable fields, an empty ParamArray) still needs a resolvable-by-name binding, but genuinely
    // has nothing to write byte-for-byte - the allocator correctly refuses it (SessionMemorySegment's
    // own guard: a 0-byte bump-pointer/free-list "allocation" would hand the same address to the next
    // real caller). It never needs real memory, only a dictionary key distinct from every other one -
    // minted from this counter instead, walking negative so it can never collide with an allocator
    // address (those only ever grow from a non-negative offset).
    private int _nextInertAddress = -1;

    public bool TryAllocate(int size, IBindingHandle handle, out MemoryAddress address)
    {
        if (size <= 0)
        {
            address = new MemoryAddress(_nextInertAddress--);
            _handleByAddress[address] = handle;
            return true;
        }

        if (!allocator.TryAllocate(size, out address))
        {
            return false;
        }

        _handleByAddress[address] = handle;
        return true;
    }

    public bool TryRead(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out IBindingHandle? handle)
        => _handleByAddress.TryGetValue(address, out handle);

    public bool TryDeallocate(MemoryAddress address)
    {
        if (address.Value < 0)
        {
            return _handleByAddress.Remove(address);
        }

        return (_handleByAddress.Remove(address) || _bytesByAddress.Remove(address)) && allocator.TryDeallocate(address, out _);
    }

    public bool TryRebind(MemoryAddress address, IBindingHandle handle)
    {
        if (!_handleByAddress.ContainsKey(address))
        {
            return false;
        }

        _handleByAddress[address] = handle;
        return true;
    }

    public bool TryAllocateBytes(int size, out MemoryAddress address)
    {
        if (!allocator.TryAllocate(size, out address))
        {
            return false;
        }

        _bytesByAddress[address] = new byte[size];
        return true;
    }

    public bool TryReadBytes(MemoryAddress address, [NotNullWhen(true)][MaybeNullWhen(false)] out byte[]? bytes)
    {
        if (!_bytesByAddress.TryGetValue(address, out var stored))
        {
            bytes = null;
            return false;
        }

        bytes = (byte[])stored.Clone();
        return true;
    }

    public bool TryWriteBytes(MemoryAddress address, ReadOnlySpan<byte> bytes)
    {
        if (!_bytesByAddress.ContainsKey(address))
        {
            return false;
        }

        _bytesByAddress[address] = bytes.ToArray();
        return true;
    }
}
