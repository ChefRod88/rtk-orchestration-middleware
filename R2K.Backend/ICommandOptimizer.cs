namespace R2K.Backend;

/// <summary>Produces a condensed CLI string suitable for piping to shells.</summary>
public interface ICommandOptimizer
{
    string Optimize(string command);
}
