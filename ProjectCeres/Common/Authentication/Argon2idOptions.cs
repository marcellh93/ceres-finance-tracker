namespace ProjectCeres.Common.Authentication;

public sealed class Argon2idOptions
{
    public int MemorySizeKb { get; set; } = 19456;
    public int Iterations   { get; set; } = 2;
    public int Parallelism  { get; set; } = 1;
}
