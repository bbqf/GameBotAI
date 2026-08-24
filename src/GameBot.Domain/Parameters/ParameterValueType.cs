namespace GameBot.Domain.Parameters;

/// <summary>
/// The declared type of a command or sequence parameter (feature 078).
/// <para>
/// Only two types exist. <see cref="Text"/> values are substituted verbatim and may be embedded in
/// surrounding text. <see cref="Number"/> values must occupy a whole field and are parsed to the
/// target field's numeric type at resolve time; a value that cannot be parsed fails the step rather
/// than reaching a device action.
/// </para>
/// </summary>
public enum ParameterValueType {
  /// <summary>A free-text value, e.g. an ADB serial or an emulator instance name.</summary>
  Text,

  /// <summary>A whole-number value, e.g. an emulator instance index or a timeout in milliseconds.</summary>
  Number
}
