namespace GameBot.Domain.Parameters;

/// <summary>
/// How a person enters the value of a parameter (feature 168). The kind changes the form control and
/// the rules for the value. It does not change the value type: a binding always stores the technical value.
/// </summary>
public enum ParameterInputKind {
  /// <summary>A free-text box. Valid for a text parameter.</summary>
  Text,

  /// <summary>A number box with an optional range, unit, and pattern. Valid for a number parameter.</summary>
  Number,

  /// <summary>A list of options. Each option has a readable label and a technical value.</summary>
  Choice,

  /// <summary>A picker for one image of a short list that the author allows. Valid for a text parameter.</summary>
  Image
}
