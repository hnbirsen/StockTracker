namespace StockTracker.Shared.Contracts.Messaging;

// Kuyruk isimlendirme kuralı: her marka kendi izole kuyruğuna sahiptir,
// böylece bir markanın scraper'ı tıkanınca diğer markaların kuyrukları etkilenmez.
public static class QueueNaming
{
    public static string StockCheckQueue(string brandName) => $"stock.check.{brandName.Trim().ToLowerInvariant()}";
}
