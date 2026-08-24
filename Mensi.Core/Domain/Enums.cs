namespace Mensi.Core.Domain;

public enum CervicalMucus : short { Dry = 0, Sticky = 1, Creamy = 2, EggWhite = 3 }
public enum LhTest : short { Negative = 0, Positive = 1, Peak = 2 }
public enum CrampType : short { Abdomen = 0, Back = 1, Breast = 2 }
public enum FlowIntensity : short { None = 0, Spotting = 1, Light = 2, Medium = 3, Heavy = 4 }
public enum Mood : short { Cheerful = 0, Calm = 1, Irritable = 2, Tired = 3, Sad = 4, Anxious = 5, Longing = 6 }
public enum TimingLabel { Weak, Medium, Good }
public enum ConfidenceLevel { Low, Medium, High }
public enum DayCategory { PreCycle, Menstruation, Follicular, Fertile, Ovulation, Luteal, PredictedPeriod, Unknown }
