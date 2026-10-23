namespace EggPdf.Pdf;

internal sealed class PdfParseBudgetExceededException : PdfFormatException
{
    public PdfParseBudgetExceededException(string message) : base(message)
    {
    }
}

internal sealed class PdfParseBudget
{
    private const long DictionaryCost = 160;
    private const long ArrayCost = 64;
    private const long TextCost = 40;
    private const long ScalarCost = 32;
    private const long MaterializedAllowance = 256L * 1024 * 1024;
    private const long ScannedAllowance = 64L * 1024 * 1024;
    private const int MaterializedPerInputByte = 16;
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

    public void Materialized(PdfObject value)
    {
        _materialized += Cost(value);
        if (_materialized > _materializedLimit)
            throw new PdfParseBudgetExceededException("The document holds far more objects than its size allows.");
    }

    public static long RetainedBytes(PdfObject value)
    {
        switch (value)
        {
            case PdfDictionary dictionary:
                long dictionaryBytes = DictionaryCost;
                foreach (var pair in dictionary.Entries) dictionaryBytes += 2L * pair.Key.Length + RetainedBytes(pair.Value);
                return dictionaryBytes;
            case PdfArray array:
                long arrayBytes = ArrayCost + 8L * array.Items.Count;
                foreach (var item in array.Items) arrayBytes += RetainedBytes(item);
                return arrayBytes;
            case PdfStream stream:
                return ScalarCost + RetainedBytes(stream.Dictionary);
            default:
                return Cost(value);
        }
    }

    private static long Cost(PdfObject value) => value switch
    {
        PdfDictionary => DictionaryCost,
        PdfArray => ArrayCost,
        PdfString text => TextCost + text.Bytes.Length,
        PdfName name => TextCost + 2L * name.Value.Length,
        _ => ScalarCost,
    };

    public void Scanned(long bytes)
    {
        if (bytes <= 0) return;
        _scanned += bytes;
        if (_scanned > _scannedLimit)
            throw new PdfParseBudgetExceededException("Reading the document needs far more work than its size allows.");
    }
}
