# Using ZatcaEGS Without Manager.io

ZatcaEGS is built around **Manager.io** integration, but the core ZATCA e-invoicing logic lives in the **`Zatca.eInvoice`** library. You can use this library **directly** in your own app (API, console, desktop) without Manager.

---

## Overview

| Component | Manager.io | Standalone (no Manager) |
|-----------|------------|--------------------------|
| **Relay UI** (`/relay`, Setup wizard) | ✅ Used | ❌ Not used |
| **`Zatca.eInvoice`** (signing, CSR, UBL) | ✅ Used | ✅ Use directly |
| **Invoice data** | Manager form payload | Your own JSON / domain models |
| **Certificate storage** | Manager custom fields | Your app (DB, config, secrets) |

---

## Option 1: Use `Zatca.eInvoice` in Your Own App (Recommended)

Reference the **`Zatca.eInvoice`** project and:

1. **Onboard your device** with ZATCA (CSR → CCSID → PCSID).
2. **Build UBL `Invoice`** objects from your data.
3. **Sign** with `InvoiceGenerator` (PCSID cert + private key).
4. **Submit** to ZATCA (compliance check, clearance, reporting) via `HttpClient`.

### 1.1 Add Project Reference

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <!-- ... -->
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="path/to/Zatca.eInvoice/Zatca.eInvoice.csproj" />
  </ItemGroup>
</Project>
```

### 1.2 Device Onboarding (One-Time)

- Use **`CsrGenerator`** to create CSR + EC private key (see `CsrGenerationDto`).
- Call ZATCA **compliance** API with CSR + OTP → get **CCSID** (token + secret).
- Run **compliance checks** (sample invoices) if required by your invoice type.
- Call ZATCA **production** API → get **PCSID** (token + secret).
- Store **PCSID** (base64 cert) and **private key** (PEM) securely; you need them for signing.

### 1.3 Build and Sign an Invoice

- Create an **`Invoice`** (UBL) with supplier, customer, lines, tax totals, etc.  
  See `Zatca.eInvoice.Models` and the sample in **`Zatca.eInvoice.Test`**.
- Instantiate **`InvoiceGenerator`** with:
  - `Invoice` object  
  - PEM string (from PCSID base64)  
  - EC private key PEM  
- Call **`GetSignedInvoiceResult()`** → you get:
  - `InvoiceHash`, `Base64SignedInvoice`, `Base64QrCode`, `XmlFileName`, `RequestApi`.

### 1.4 Submit to ZATCA

- **Compliance check**: `POST` to compliance endpoint with `RequestApi` (uuid, invoiceHash, invoice), Basic auth = CCSID token:secret.
- **Clearance / Reporting**: `POST` to clearance or reporting endpoint with same payload, Basic auth = **PCSID** token:secret, and appropriate headers (e.g. `Clearance-Status`).

Base URLs (use correct environment: developer-portal / simulation / core):

- Compliance: `https://gw-fatoora.zatca.gov.sa/e-invoicing/{env}/compliance`
- Production CSIDs: `https://gw-fatoora.zatca.gov.sa/e-invoicing/{env}/production/csids`
- Compliance invoices: `https://gw-fatoora.zatca.gov.sa/e-invoicing/{env}/compliance/invoices`
- Clearance: `https://gw-fatoora.zatca.gov.sa/e-invoicing/{env}/invoices/clearance/single`
- Reporting: `https://gw-fatoora.zatca.gov.sa/e-invoicing/{env}/invoices/reporting/single`

---

## Option 2: Run the Standalone Example

A minimal **standalone console app** is in **`Zatca.StandaloneExample`**. It uses **only** `Zatca.eInvoice` (no Manager, no ZatcaEGS web app).

```bash
dotnet run --project Zatca.StandaloneExample
```

It demonstrates:

- CSR generation and device onboarding (simulation).
- Building a sample **`Invoice`** and signing it.
- Compliance check request.

Use it as a template and replace the sample data with your own.

---

## Option 3: Use the Existing Test Project

**`Zatca.eInvoice.Test`** already contains a full flow (onboarding, signing, compliance check) without Manager.  
See `Program.cs` and the `ZatcaService` class. Uncomment the relevant blocks and run:

```bash
dotnet run --project Zatca.eInvoice.Test
```

Note: the test uses **developer-portal** (simulation) endpoints and sample credentials. Adapt URLs and credentials for your environment.

---

## Data You Must Provide (Without Manager)

When not using Manager, **you** are responsible for:

| Data | Description |
|------|-------------|
| **Supplier (your business)** | CRN/TIN, address, VAT, registration name, etc. |
| **Customer** | Name, address, VAT (if applicable), etc. |
| **Invoice header** | ID, UUID, issue date/time, type, currency, etc. |
| **Lines** | Description, qty, unit price, tax category, discounts, etc. |
| **Tax totals** | VAT breakdown, amounts. |
| **Legal monetary total** | Line total, tax, allowance, payable. |
| **Certificate** | PCSID (base64) + EC private key (PEM) from onboarding. |
| **ICV / PIH** | Invoice counter and previous invoice hash (per ZATCA rules). |

The **`Invoice`** model and **`InvoiceGenerator`** expect UBL-shaped data. Map your domain model or JSON into `Invoice` before signing.

---

## Summary

- **With Manager.io**: Use ZatcaEGS web app; set relay URL in Manager and follow the setup wizard.
- **Without Manager.io**: Use **`Zatca.eInvoice`** in your own app. Implement onboarding, build `Invoice` from your data, sign with `InvoiceGenerator`, and call ZATCA APIs yourself. Use **`Zatca.StandaloneExample`** or **`Zatca.eInvoice.Test`** as reference.
