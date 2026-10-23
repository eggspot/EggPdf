namespace EggPdf.Pdf;

internal sealed class PdfParseBudgetExceededException : PdfFormatException
{
    public PdfParseBudgetExceededException(string message) : base(message)
    {
    }
}

internal sealed class PdfParseBudget
{
    private const long ObjectOverheadBytes = 48;
    private const long MaterializedAllowance = 32L * 1024 * 1024;
    private const long ScannedAllowance = 64L * 1024 * 1024;
    private const int MaterializedPerInputByte = 4;
    private const int ScannedPerInputByte = 8;
    private const int ScannedPerDecodedByte = 2;

    private readonly long _materializedLimit;
    private long _scannedLimit;
    private long _materialized;
    private long _scanned;

    public PdfParseBudget(long inputLength)
    {
        _materializedLimit = MaterializedPerInputByte * inputLength + MaterializedAllowance;
        _scannedLimit = ScannedPerInputByte * inputLength + ScannedAllowance;
    }

    public void AllowScanningDecoded(long decodedBytes) => _scannedLimit += ScannedPerDecodedByte * decodedBytes;

    public void Materialized(long payloadBytes)
    {
        _materialized += ObjectOverheadBytes + payloadBytes;
        if (_materialized > _materializedLimit)
            throw new PdfParseBudgetExceededException("The document holds far more objects than its size allows.");
    }

    public void Scanned(long bytes)
    {
        if (bytes <= 0) return;
        _scanned += bytes;
        if (_scanned > _scannedLimit)
            throw new PdfParseBudgetExceededException("Reading the document needs far more work than its size allows.");
    }
}
