namespace HorizontalDirectionalDrilling;

public static class Ids
{
    public static class Buildings
    {
        public static readonly Proto.ID DrillingPad = Create("Building", "HorizontalDrillingPad");
        public static readonly Proto.ID ReceivingPad = Create("Building", "HorizontalDrillingReceivingPad");
    }

    public static class Products
    {
        public static readonly Proto.ID DrillPipe = Create("Product", "DirectionalDrillPipe");
        public static readonly Proto.ID DrillingFluid = Create("Product", "DirectionalDrillingFluid");
    }

    public static class Research
    {
        public static readonly Proto.ID HorizontalDirectionalDrilling = Create(
            "Technology",
            "HorizontalDirectionalDrilling");
    }

    private static Proto.ID Create(string type, string id)
    {
        return new Proto.ID(type + id);
    }
}
