using ProjectCeres.Models;

namespace ProjectCeres.Services;

public class ImportParserFactory(CsvImportParser csvParser, ExcelImportParser excelParser)
{
    public IImportParser GetParser(ImportFormat format) => format switch
    {
        ImportFormat.Csv   => csvParser,
        ImportFormat.Excel => excelParser,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No parser registered for this format.")
    };
}
