namespace GhostBuildOption;

public static class Ids
{
    public static class Technology
    {
        public static readonly Proto.ID GhostTool;
        public static readonly Proto.ID GhostPlansTool;
        private static Proto.ID createTechId(string id)
        {
            return new Proto.ID("Technology" + id);
        }

        static Technology()
        {
            GhostTool = createTechId("GhostTool");
            GhostPlansTool = createTechId("GhostPlansTool");
        }
    }
}
