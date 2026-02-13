namespace GameBot.Service.Services.Installer;

internal sealed class FirewallPolicyService {
  public static FirewallDecision Evaluate(bool canApplyPrivateFirewallRules, bool confirmHostDefaultFallback) {
    if (canApplyPrivateFirewallRules) {
      return new FirewallDecision {
        FirewallScope = "privateNetworkOnly",
        CanProceed = true
      };
    }

    var warning = "Installer could not apply private-network firewall rules; host-default firewall policy will be used.";
    if (!confirmHostDefaultFallback) {
      return new FirewallDecision {
        FirewallScope = "hostDefault",
        CanProceed = false,
        Warnings = [warning],
        Errors = ["Explicit confirmation is required to continue with host-default firewall behavior."]
      };
    }

    return new FirewallDecision {
      FirewallScope = "hostDefault",
      CanProceed = true,
      Warnings = [warning]
    };
  }
}

internal sealed class FirewallDecision {
  public string FirewallScope { get; set; } = "privateNetworkOnly";
  public bool CanProceed { get; set; }
  public IReadOnlyList<string> Warnings { get; set; } = [];
  public IReadOnlyList<string> Errors { get; set; } = [];
}
