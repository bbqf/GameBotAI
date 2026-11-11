namespace GameBot.Service.Security;

/// <summary>Where a request comes from (feature 187, research R6).</summary>
internal enum RequestOrigin {
  /// <summary>The request comes from the service machine.</summary>
  LocalMachine,

  /// <summary>The request comes from a device in the home network.</summary>
  HomeNetwork,

  /// <summary>The request comes from the internet, or the service cannot tell. A doubt gives this value.</summary>
  Internet
}
