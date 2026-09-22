using System;

namespace EggPdf.Pdf;

/// <summary>
/// Invoice data for the Factur-X/ZUGFeRD MINIMUM profile -- the smallest of the profile ladder
/// (MINIMUM, BASIC WL, BASIC, EN 16931/Comfort, EXTENDED). MINIMUM covers only what the
/// guideline requires (parties, totals, no line items) and is not EN 16931 compliant on its
/// own; BASIC/Comfort support (line items, full tax breakdown) is a natural extension of this
/// same model, not a different one.
/// </summary>
public class FacturXInvoice
{
    /// <summary>Invoice number (<c>ram:ID</c>).</summary>
    public string InvoiceNumber { get; set; } = "";

    /// <summary>Invoice issue date.</summary>
    public DateTime IssueDate { get; set; }

    /// <summary>ISO 4217 currency code, e.g. "EUR".</summary>
    public string CurrencyCode { get; set; } = "EUR";

    /// <summary>Seller (supplier) name.</summary>
    public string SellerName { get; set; } = "";

    /// <summary>Seller's ISO 3166-1 alpha-2 country code, e.g. "FR". Optional.</summary>
    public string? SellerCountryCode { get; set; }

    /// <summary>Seller's VAT identification number, e.g. "FR34999888779". Optional.</summary>
    public string? SellerVatId { get; set; }

    /// <summary>Buyer (customer) name.</summary>
    public string BuyerName { get; set; } = "";

    /// <summary>Buyer-assigned reference for this transaction. Optional.</summary>
    public string? BuyerReference { get; set; }

    /// <summary>Total amount before tax.</summary>
    public decimal TaxBasisTotal { get; set; }

    /// <summary>Total tax amount.</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>Grand total including tax.</summary>
    public decimal GrandTotal { get; set; }

    /// <summary>Amount due for payment.</summary>
    public decimal DuePayableAmount { get; set; }
}
