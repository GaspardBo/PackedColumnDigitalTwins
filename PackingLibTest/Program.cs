using PackingLibrary;

PackingManager packingManager = PackingManager.Load("./../../../../Packings/library.json");

Console.WriteLine($"Counted packings: {packingManager.Count}");
Console.WriteLine(packingManager.GetPackingsByFamily("RASCHIG_SUPER_RING").First().Name);
