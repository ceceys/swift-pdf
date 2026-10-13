namespace SwiftPdf.Core;

/// <summary>
/// Kuveyt Türk SWIFT metninde geçen etiketler: ISO 20022 pacs.008 (CBPR+) eleman adlarının bankanın yazdığı
/// biçimi, iş üstbilgisi (BAH) ve Interventions alanları. Dar sütunda alt satıra kayan bir etiketin parçalarını,
/// alt alta duran iki ayrı başlıktan ayırmak için kullanılır.
/// </summary>
public static class KnownLabels
{
    private static readonly HashSet<string> Set = new(StringComparer.Ordinal)
    {
        // Üstbilgi
        "From", "To", "FI Id", "Financial Institution Identification", "BICFI", "Biz Msg Idr",
        "Message Definition Identifier", "Biz Svc", "Creation Date", "Related", "Priority", "Copy Duplicate",
        "Possible Duplicate", "Business Application Header", "Character Set",
        // Grup başlığı
        "FI To FI Customer Credit Transfer", "Financial Institution Credit Transfer", "Group Header",
        "Message Identification", "Creation Date Time", "Number Of Transactions", "Control Sum",
        "Total Interbank Settlement Amount", "Batch Booking", "Settlement Information", "Settlement Method",
        "Settlement Account", "Clearing System", "Instructing Reimbursement Agent",
        "Instructing Reimbursement Agent Account", "Instructed Reimbursement Agent",
        "Instructed Reimbursement Agent Account", "Third Reimbursement Agent", "Third Reimbursement Agent Account",
        // İşlem
        "Credit Transfer Transaction Information", "Payment Identification", "Instruction Identification",
        "End To End Identification", "Transaction Identification", "UETR", "Clearing System Reference",
        "Payment Type Information", "Instruction Priority", "Clearing Channel", "Service Level", "Code",
        "Proprietary", "Local Instrument", "Category Purpose", "Interbank Settlement Amount",
        "Interbank Settlement Date", "Settlement Priority", "Settlement Time Indication", "Debit Date Time",
        "Credit Date Time", "Settlement Time Request", "CLS Time", "Till Time", "From Time", "Reject Time",
        "Acceptance Date Time", "Pooling Adjustment Date", "Instructed Amount", "Exchange Rate", "Charge Bearer",
        "Charges Information", "Amount", "Agent", "Previous Instructing Agent1", "Previous Instructing Agent1 Account",
        "Previous Instructing Agent2", "Previous Instructing Agent2 Account", "Previous Instructing Agent3",
        "Previous Instructing Agent3 Account", "Instructing Agent", "Instructed Agent", "Intermediary Agent1",
        "Intermediary Agent1 Account", "Intermediary Agent2", "Intermediary Agent2 Account", "Intermediary Agent3",
        "Intermediary Agent3 Account", "Ultimate Debtor", "Initiating Party", "Debtor", "Debtor Account",
        "Debtor Agent", "Debtor Agent Account", "Creditor Agent", "Creditor Agent Account", "Creditor",
        "Creditor Account", "Ultimate Creditor", "Instruction For Creditor Agent", "Instruction For Next Agent",
        "Instruction Information", "Purpose", "Regulatory Reporting", "Debit Credit Reporting Indicator",
        "Authority", "Details", "Type", "Date", "Information", "Tax", "Related Remittance Information",
        "Remittance Identification", "Remittance Location Details", "Method", "Electronic Address",
        "Remittance Information", "Unstructured", "Structured", "Referred Document Information",
        "Referred Document Amount", "Creditor Reference Information", "Invoicer", "Invoicee",
        "Additional Remittance Information", "Supplementary Data",
        // Kurum ve taraf kimliği
        "Clearing System Member Identification", "Clearing System Identification", "Member Identification",
        "LEI", "Name", "Postal Address", "Other", "Identification", "Scheme Name", "Issuer",
        "Branch Identification", "Organisation Identification", "Private Identification", "Any BIC",
        "Date And Place Of Birth", "Birth Date", "Province Of Birth", "City Of Birth", "Country Of Birth",
        "Country Of Residence", "Contact Details",
        // Adres
        "Address Type", "Department", "Sub Department", "Street Name", "Building Number", "Building Name",
        "Floor", "Post Box", "Room", "Post Code", "Town Name", "Town Location Name", "District Name",
        "Country Sub Division", "Country", "Address Line",
        // Hesap
        "IBAN", "Currency", "Proxy",
        // Interventions
        "Category", "Creation Time (Paygate Maestro)", "Application", "Operator(s)", "Message View", "Text",
    };

    private static readonly string[] Prefixes = Set.Select(s => s + " ").ToArray();

    // Mesaj türü satırı: "Swift INPUT FIN pacs.008.001.08"
    private static readonly System.Text.RegularExpressions.Regex MessageType =
        new(@"^Swift (INPUT|OUTPUT) FIN [a-z]{4}\.\d{3}\.\d{3}\.\d{2}$");
    private static readonly System.Text.RegularExpressions.Regex MessageTypePart =
        new(@"^Swift( (INPUT|OUTPUT)( FIN)?)?$");

    public static bool IsKnown(string label) => Set.Contains(label) || MessageType.IsMatch(label);

    /// <summary>Bilinen bir etiketin baştan bir parçası mı (ör. "Financial Institution")?</summary>
    public static bool IsPrefixOfKnown(string text)
    {
        if (MessageTypePart.IsMatch(text)) return true;
        var p = text + " ";
        foreach (var k in Prefixes)
            if (k.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }
}
