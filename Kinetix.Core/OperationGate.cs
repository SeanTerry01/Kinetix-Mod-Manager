using System;
using System.Threading;

namespace KinetixModManager;

/// <summary>
/// Counts operations in progress, so something that must not happen during one can ask. Several can run at once, and
/// the gate is busy until the last has finished.
/// </summary>
public sealed class OperationGate
{
	private int _running;

	/// <summary>True while at least one operation is in progress.</summary>
	public bool IsBusy => Volatile.Read(ref _running) > 0;

	/// <summary>Starts an operation, which lasts until the returned object is disposed.</summary>
	public IDisposable Begin()
	{
		Interlocked.Increment(ref _running);
		return new Scope(this);
	}

	private sealed class Scope : IDisposable
	{
		private readonly OperationGate _gate;
		private int _disposed;

		public Scope(OperationGate gate) => _gate = gate;

		public void Dispose()
		{
			// Disposed twice, a plain decrement would mark the gate free while another operation was still running.
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
				Interlocked.Decrement(ref _gate._running);
		}
	}
}
