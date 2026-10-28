namespace Schadenfreude
{
    public enum Reaction { None = -1, Satisfaction = 0, Vindication = 1, Mixed = 2 }

    // Pure decision function: no game state, random rolls, opinions queried from thought workers,
    // or claims that the victim objectively deserved the setback.
    public static class Appraisal
    {
        public static Reaction Evaluate(int opinion, bool grievance, bool rivalCrew,
            bool sameCrew, bool kind, bool serious)
        {
            if (opinion >= 20 || kind) return Reaction.None;
            bool personal = opinion <= -20;
            bool social = (grievance || rivalCrew) && opinion <= 0;
            if (!personal && !social) return Reaction.None;
            // Solidarity usually wins. A deep personal grievance can overcome it, uneasily.
            if (sameCrew && !(opinion <= -60 && grievance)) return Reaction.None;
            if (serious || sameCrew) return Reaction.Mixed;
            return opinion <= -60 && grievance ? Reaction.Vindication : Reaction.Satisfaction;
        }
    }
}
