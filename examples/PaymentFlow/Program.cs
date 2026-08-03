using System;
using System.Globalization;
using Requisite;

static bool TryParseCustomerId(string input, out int id) =>
    int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out id);

static void Approve(Certain _, Trusted<int> customerId) =>
    Console.WriteLine($"Approved customer {customerId.Value}.");

var input = Untrusted.From("42");
if (!Trust.TrySanitize(input, TryParseCustomerId, out Trusted<int>? customerId))
{
    Console.WriteLine("Rejected customer ID.");
    return;
}

var quote = Fresh.Fetch(4.99m, TimeSpan.FromSeconds(30));
quote.Read().Switch(
    price => Console.WriteLine($"Current quote: {price:C}."),
    stale => Console.WriteLine($"Quote expired at age {stale.Metadata.Age}."));

Confident.Create(value: true, confidence: 0.97).Gate().Switch(
    (proof, approved) =>
    {
        if (approved)
        {
            Approve(proof, customerId);
        }
    },
    _ => Console.WriteLine("Request manual review."),
    _ => Console.WriteLine("Record only."));
