namespace SemanticKernel.Connectors.Oracle;

internal static class AsyncEnumerable
{
    public static async ValueTask<T?> FirstOrDefaultAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            return item;
        }

        return default;
    }
}