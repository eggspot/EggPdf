using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// Invoice data for Factur-X/ZUGFeRD. The profile is derived from what's populated, not chosen
/// explicitly: no <see cref="LineItems"/> produces the MINIMUM profile (parties, totals, no
/// lines); one or more line items produces an EN 16931 (Comfort)-conformant document, since
/// per-line tax detail is what EN 16931 actually requires beyond BASIC. When line items are set,
/// <see cref="TaxBasisTotal"/>/<see cref="TaxTotal"/>/<see cref="GrandTotal"/>/<see cref="DuePayableAmount"/>
/// are computed from the lines (ignoring whatever these properties were set to) rather than
/// trusted as separately-supplied values that could silently disagree with the line-item math --
/// a real invoice where the header totals don't match the sum of its lines is legally wrong, not
/// just a documentation nicety.
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

    /// <summary>Total amount before tax. Ignored (computed instead) when <see cref="LineItems"/> is non-empty.</summary>
    public decimal TaxBasisTotal { get; set; }

    /// <summary>Total tax amount. Ignored (computed instead) when <see cref="LineItems"/> is non-empty.</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>Grand total including tax. Ignored (computed instead) when <see cref="LineItems"/> is non-empty.</summary>
    public decimal GrandTotal { get; set; }

    /// <summary>Amount due for payment. Ignored (computed instead) when <see cref="LineItems"/> is non-empty.</summary>
    public decimal DuePayableAmount { get; set; }

    /// <summary>
    /// Invoice line items. Empty produces the MINIMUM profile; one or more produces an
    /// EN 16931-conformant document (line detail + a per-VAT-category/rate tax breakdown
    /// computed from these lines).
    /// </summary>
    public List<FacturXLineItem> LineItems { get; set; } = new();
}

/// <summary>One line of a Factur-X/ZUGFeRD invoice (EN 16931 profile and above).</summary>
public class FacturXLineItem
{
    /// <summary>Line number, e.g. "1".</summary>
    public string LineId { get; set; } = "";

    /// <summary>Product or service name.</summary>
    public string ItemName { get; set; } = "";

    /// <summary>Net price per unit, before tax.</summary>
    public decimal NetUnitPrice { get; set; }

    /// <summary>Billed quantity.</summary>
    public decimal BilledQuantity { get; set; }

    /// <summary>UN/ECE Recommendation 20 unit code, e.g. "C62" (one/piece), "HUR" (hour), "KGM" (kilogram). Defaults to "C62".</summary>
    public string UnitCode { get; set; } = "C62";

    /// <summary>Net line total (typically <see cref="NetUnitPrice"/> &#215; <see cref="BilledQuantity"/>, but not required to be -- discounts etc. can make it otherwise).</summary>
    public decimal LineTotalAmount { get; set; }

    /// <summary>VAT category code (UNTDID 5305), e.g. "S" (standard rate), "Z" (zero rated), "E" (exempt). Defaults to "S".</summary>
    public string VatCategoryCode { get; set; } = "S";

    /// <summary>VAT rate as a percentage, e.g. 20 for 20%.</summary>
    public decimal VatRatePercent { get; set; }
}
