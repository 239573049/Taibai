namespace Taibai.Core;

public class SafeWriteStream(Stream inner) : DelegatingStream(inner)
{
    private readonly SemaphoreSlim semaphoreSlim = new(1, 1);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        try
        {
            await this.semaphoreSlim.WaitAsync(CancellationToken.None);
            await base.WriteAsync(source, cancellationToken);
            await this.FlushAsync(cancellationToken);
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    public override ValueTask DisposeAsync()
    {
        this.semaphoreSlim.Dispose();
        return this.Inner.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        this.semaphoreSlim.Dispose();
        this.Inner.Dispose();
    }
}