/*
 * Zatca.StandaloneExample
 *
 * Minimal example of using Zatca.eInvoice WITHOUT Manager.io.
 * Run: dotnet run --project Zatca.StandaloneExample
 *
 * Flow: Onboard device (CSR -> CCSID -> PCSID) -> Build Invoice -> Sign -> Compliance check
 */

using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Zatca.eInvoice;
using Zatca.eInvoice.Helpers;
using Zatca.eInvoice.Models;

Console.OutputEncoding = Encoding.UTF8;

const string ComplianceBase = "https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal";
const string ComplianceCsidUrl = ComplianceBase + "/compliance";
const string ProductionCsidUrl = ComplianceBase + "/production/csids";
const string ComplianceCheckUrl = ComplianceBase + "/compliance/invoices";

// ZATCA sandbox OTP (use your own when onboarding a real device)
const string OTP = "12345";

using var http = new HttpClient();

// ----- Step 1: Generate CSR and private key -----
Console.WriteLine("1. Generating CSR and private key...\n");

var csrDto = new CsrGenerationDto
{
    CommonName = "TST-886431145-399999999900003",
    SerialNumber = "1-TST|2-TST|3-ed22f1d8-e6a2-1118-9b58-d9a8f11e445f",
    OrganizationIdentifier = "399999999900003",
    OrganizationUnitName = "Riyadh Branch",
    OrganizationName = "Maximum Speed Tech Supply LTD",
    CountryName = "SA",
    InvoiceType = "1100",
    LocationAddress = "RRRD2929",
    IndustryBusinessCategory = "Supply activities"
};

var csrGen = new CsrGenerator();
var (csr, privateKeyPem, csrErrors) = csrGen.GenerateCsrAndPrivateKey(csrDto, EnvironmentType.NonProduction, false);
if (csrErrors?.Count > 0)
{
    Console.WriteLine("CSR errors: " + string.Join("; ", csrErrors));
    return 1;
}
Console.WriteLine("CSR generated.");

// ----- Step 2: Get CCSID (compliance CSID) -----
Console.WriteLine("\n2. Requesting CCSID from ZATCA...\n");

http.DefaultRequestHeaders.Clear();
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
http.DefaultRequestHeaders.Add("OTP", OTP);
http.DefaultRequestHeaders.Add("Accept-Version", "V2");

var csrPayload = JsonConvert.SerializeObject(new { csr });
var csrResponse = await http.PostAsync(ComplianceCsidUrl, new StringContent(csrPayload, Encoding.UTF8, "application/json"));
csrResponse.EnsureSuccessStatusCode();

var csidResult = JsonConvert.DeserializeObject<ZatcaTokenResult>(await csrResponse.Content.ReadAsStringAsync())
    ?? throw new InvalidOperationException("Failed to parse CCSID response.");
var ccsidToken = csidResult.BinarySecurityToken ?? "";
var ccsidSecret = csidResult.Secret ?? "";
var complianceRequestId = csidResult.RequestID ?? "";
Console.WriteLine("CCSID received.");

// ----- Step 3: Get PCSID (production CSID) -----
Console.WriteLine("\n3. Requesting PCSID from ZATCA...\n");

http.DefaultRequestHeaders.Clear();
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
http.DefaultRequestHeaders.Add("Accept-Version", "V2");
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ccsidToken}:{ccsidSecret}")));

var pcsidPayload = JsonConvert.SerializeObject(new { compliance_request_id = complianceRequestId });
var pcsidResponse = await http.PostAsync(ProductionCsidUrl, new StringContent(pcsidPayload, Encoding.UTF8, "application/json"));
pcsidResponse.EnsureSuccessStatusCode();

var pcsidResult = JsonConvert.DeserializeObject<ZatcaTokenResult>(await pcsidResponse.Content.ReadAsStringAsync())
    ?? throw new InvalidOperationException("Failed to parse PCSID response.");
var pcsidToken = pcsidResult.BinarySecurityToken ?? "";
var pcsidSecret = pcsidResult.Secret ?? "";
Console.WriteLine("PCSID received.");

// ----- Step 4: Build a sample Invoice (UBL) -----
Console.WriteLine("\n4. Building sample Invoice...\n");

// Default PIH from ZATCA samples (first invoice in chain)
const string defaultPih = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==";

var invoice = new Invoice
{
    ProfileID = "reporting:1.0",
    ID = new ID("SME00010"),
    UUID = Guid.NewGuid().ToString(),
    IssueDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
    IssueTime = DateTime.UtcNow.ToString("HH:mm:ss"),
    InvoiceTypeCode = new InvoiceTypeCode(InvoiceType.TaxInvoice, "0200000"),
    DocumentCurrencyCode = "SAR",
    TaxCurrencyCode = "SAR",

    AdditionalDocumentReference =
    [
        new AdditionalDocumentReference { ID = new ID("ICV"), UUID = "1" },
        new AdditionalDocumentReference
        {
            ID = new ID("PIH"),
            Attachment = new Attachment
            {
                EmbeddedDocumentBinaryObject = new EmbeddedDocumentBinaryObject(defaultPih) { MimeCode = "text/plain" }
            }
        }
    ],

    AccountingSupplierParty = new AccountingSupplierParty
    {
        Party = new Party
        {
            PartyIdentification = new PartyIdentification { ID = new ID("CRN", null, "1010010000") },
            PostalAddress = new PostalAddress
            {
                StreetName = "Prince Sultan",
                BuildingNumber = "2322",
                CitySubdivisionName = "Al-Murabba",
                CityName = "Riyadh",
                PostalZone = "23333",
                Country = new Country("SA")
            },
            PartyTaxScheme = new PartyTaxScheme
            {
                CompanyID = "399999999900003",
                TaxScheme = new TaxScheme { ID = new ID("VAT") }
            },
            PartyLegalEntity = new PartyLegalEntity("Maximum Speed Tech Supply LTD")
        }
    },

    AccountingCustomerParty = new AccountingCustomerParty
    {
        Party = new Party
        {
            PostalAddress = new PostalAddress
            {
                StreetName = "Salah Al-Din",
                BuildingNumber = "1111",
                CitySubdivisionName = "Al-Murooj",
                CityName = "Riyadh",
                PostalZone = "12222",
                Country = new Country("SA")
            },
            PartyTaxScheme = new PartyTaxScheme
            {
                CompanyID = "399999999800003",
                TaxScheme = new TaxScheme { ID = new ID("VAT") }
            },
            PartyLegalEntity = new PartyLegalEntity("Fatoora Samples LTD")
        }
    },

    Delivery = new Delivery
    {
        ActualDeliveryDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        LatestDeliveryDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
    },

    PaymentMeans = new PaymentMeans("10"),

    AllowanceCharge = new AllowanceCharge
    {
        ChargeIndicator = false,
        AllowanceChargeReason = "discount",
        Amount = new Amount("SAR", 0.00),
        TaxCategory =
        [
            new TaxCategory
            {
                ID = new ID("UN/ECE 5305", "6", "S"),
                Percent = 15,
                TaxScheme = new TaxScheme { ID = new ID("UN/ECE 5153", "6", "VAT") }
            },
            new TaxCategory
            {
                ID = new ID("UN/ECE 5305", "6", "S"),
                Percent = 15,
                TaxScheme = new TaxScheme { ID = new ID("UN/ECE 5153", "6", "VAT") }
            }
        ]
    },

    TaxTotal =
    [
        new TaxTotal { TaxAmount = new Amount("SAR", 30.15) },
        new TaxTotal
        {
            TaxAmount = new Amount("SAR", 30.15),
            TaxSubtotal =
            [
                new TaxSubtotal
                {
                    TaxableAmount = new Amount("SAR", 201.00),
                    TaxAmount = new Amount("SAR", 30.15),
                    TaxCategory = new TaxCategory
                    {
                        ID = new ID("UN/ECE 5305", "6", "S"),
                        Percent = 15.00,
                        TaxScheme = new TaxScheme { ID = new ID("UN/ECE 5153", "6", "VAT") }
                    }
                }
            ]
        }
    ],

    LegalMonetaryTotal = new LegalMonetaryTotal
    {
        LineExtensionAmount = new Amount("SAR", 201.00),
        TaxExclusiveAmount = new Amount("SAR", 201.00),
        TaxInclusiveAmount = new Amount("SAR", 231.15),
        AllowanceTotalAmount = new Amount("SAR", 0.00),
        PrepaidAmount = new Amount("SAR", 0.00),
        PayableAmount = new Amount("SAR", 231.15)
    },

    InvoiceLine =
    [
        new InvoiceLine
        {
            ID = new ID("1"),
            InvoicedQuantity = new InvoicedQuantity("PCE", 33),
            LineExtensionAmount = new Amount("SAR", 99.00),
            TaxTotal = new TaxTotal
            {
                TaxAmount = new Amount("SAR", 14.85),
                RoundingAmount = new Amount("SAR", 113.85)
            },
            Item = new Item
            {
                Name = "Product A",
                ClassifiedTaxCategory = new ClassifiedTaxCategory
                {
                    ID = new ID("S"),
                    Percent = 15.00,
                    TaxScheme = new TaxScheme { ID = new ID("VAT") }
                }
            },
            Price = new Price
            {
                PriceAmount = new Amount("SAR", 3.00),
                AllowanceCharge = new AllowanceCharge
                {
                    ChargeIndicator = true,
                    AllowanceChargeReason = "discount",
                    Amount = new Amount("SAR", 0.00)
                }
            }
        },
        new InvoiceLine
        {
            ID = new ID("2"),
            InvoicedQuantity = new InvoicedQuantity("PCE", 3),
            LineExtensionAmount = new Amount("SAR", 102.00),
            TaxTotal = new TaxTotal
            {
                TaxAmount = new Amount("SAR", 15.30),
                RoundingAmount = new Amount("SAR", 117.30)
            },
            Item = new Item
            {
                Name = "Product B",
                ClassifiedTaxCategory = new ClassifiedTaxCategory
                {
                    ID = new ID("S"),
                    Percent = 15.00,
                    TaxScheme = new TaxScheme { ID = new ID("VAT") }
                }
            },
            Price = new Price
            {
                PriceAmount = new Amount("SAR", 34.00),
                AllowanceCharge = new AllowanceCharge
                {
                    ChargeIndicator = true,
                    AllowanceChargeReason = "discount",
                    Amount = new Amount("SAR", 0.00)
                }
            }
        }
    ]
};

// ----- Step 5: Sign the invoice -----
Console.WriteLine("5. Signing invoice with PCSID...\n");

var certPem = Encoding.UTF8.GetString(Convert.FromBase64String(pcsidToken));
var generator = new InvoiceGenerator(invoice, certPem, privateKeyPem);
var signed = generator.GetSignedInvoiceResult();

Console.WriteLine($"   Invoice hash: {signed.InvoiceHash?.Substring(0, Math.Min(32, signed.InvoiceHash?.Length ?? 0))}...");
Console.WriteLine($"   QR code length: {signed.Base64QrCode?.Length ?? 0} chars");

// ----- Step 6: Compliance check -----
Console.WriteLine("\n6. Sending compliance check to ZATCA...\n");

http.DefaultRequestHeaders.Clear();
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en"));
http.DefaultRequestHeaders.Add("Accept-Version", "V2");
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
    Convert.ToBase64String(Encoding.ASCII.GetBytes($"{ccsidToken}:{ccsidSecret}")));

var compliancePayload = JsonConvert.SerializeObject(new
{
    uuid = signed.RequestApi.uuid,
    invoiceHash = signed.RequestApi.invoiceHash,
    invoice = signed.RequestApi.invoice
});
var complianceResponse = await http.PostAsync(ComplianceCheckUrl, new StringContent(compliancePayload, Encoding.UTF8, "application/json"));
var complianceBody = await complianceResponse.Content.ReadAsStringAsync();
var complianceJson = JsonConvert.DeserializeObject<ComplianceResult>(complianceBody);

Console.WriteLine($"   Status: {complianceResponse.StatusCode}");
Console.WriteLine($"   Clearance: {complianceJson?.ClearanceStatus ?? "N/A"}");
Console.WriteLine($"   Reporting: {complianceJson?.ReportingStatus ?? "N/A"}");
if (complianceJson?.WarningMessages?.Count > 0 || complianceJson?.ErrorMessages?.Count > 0)
{
    foreach (var w in complianceJson.WarningMessages ?? [])
        Console.WriteLine($"   Warning: {w?.Message}");
    foreach (var e in complianceJson.ErrorMessages ?? [])
        Console.WriteLine($"   Error: {e?.Message}");
}

Console.WriteLine("\nDone. No Manager.io involved.");
return 0;

// ----- DTOs for ZATCA API responses -----

internal class ZatcaTokenResult
{
    [JsonProperty("requestID")]
    public string? RequestID { get; set; }

    [JsonProperty("binarySecurityToken")]
    public string? BinarySecurityToken { get; set; }

    [JsonProperty("secret")]
    public string? Secret { get; set; }
}

internal class ComplianceResult
{
    [JsonProperty("clearanceStatus")]
    public string? ClearanceStatus { get; set; }

    [JsonProperty("reportingStatus")]
    public string? ReportingStatus { get; set; }

    [JsonProperty("warningMessages")]
    public List<DetailMsg>? WarningMessages { get; set; }

    [JsonProperty("errorMessages")]
    public List<DetailMsg>? ErrorMessages { get; set; }
}

internal class DetailMsg
{
    [JsonProperty("message")]
    public string? Message { get; set; }
}
