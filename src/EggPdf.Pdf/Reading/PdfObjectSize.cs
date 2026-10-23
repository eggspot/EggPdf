namespace EggPdf.Pdf;

internal static class PdfObjectSize
{
    private const long ObjectHeader = 24;
    private const long StringHeader = 22;
    private const long ListHeader = 40;
    private const long ArrayHeader = 24;
    private const long ReferenceSlotWithSlack = 12;
    private const long DictionaryHeader = 160;
    private const long DictionaryEntryWithSlack = 36;

    public static long Of(PdfObject value)
    {
        switch (value)
        {
            case PdfDictionary dictionary:
                long dictionaryBytes = ObjectHeader + DictionaryHeader;
                foreach (var pair in dictionary.Entries)
                    dictionaryBytes += DictionaryEntryWithSlack + StringHeader + 2L * pair.Key.Length + Of(pair.Value);
                return dictionaryBytes;
            case PdfArray array:
                long arrayBytes = ObjectHeader + ListHeader + ArrayHeader + ReferenceSlotWithSlack * array.Items.Count;
                foreach (var item in array.Items) arrayBytes += Of(item);
                return arrayBytes;
            case PdfStream stream:
                return ObjectHeader + Of(stream.Dictionary);
            case PdfName name:
                return ObjectHeader + StringHeader + 2L * name.Value.Length;
            case PdfString text:
                return ObjectHeader + ArrayHeader + text.Bytes.Length;
            case PdfNull:
                return 0;
            default:
                return ObjectHeader;
        }
    }
}
