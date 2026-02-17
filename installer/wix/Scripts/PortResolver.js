function parsePort(value) {
  if (value === null || value === undefined) {
    return -1;
  }

  var text = ("" + value).replace(/^\s+|\s+$/g, "");
  if (text.length === 0 || text.toLowerCase() === "auto") {
    return -1;
  }

  var parsed = parseInt(text, 10);
  if (isNaN(parsed) || parsed < 1 || parsed > 65535) {
    return -1;
  }

  return parsed;
}

function trimText(value) {
  return ("" + value).replace(/^\s+|\s+$/g, "");
}

function appendDebugLog(message) {
  try {
    var shell = new ActiveXObject("WScript.Shell");
    var fso = new ActiveXObject("Scripting.FileSystemObject");
    var logPath = shell.ExpandEnvironmentStrings("%TEMP%\\gamebot_interface_discovery.log");
    var stream = fso.OpenTextFile(logPath, 8, true);
    stream.WriteLine((new Date()).toUTCString() + " | " + message);
    stream.Close();
  }
  catch (ignored) {
  }
}

function parseIpv4(value) {
  var text = trimText(value);
  var match = text.match(/(\d{1,3}(?:\.\d{1,3}){3})/);
  if (!match) {
    return "";
  }

  var candidate = match[1];
  var parts = candidate.split(".");
  if (parts.length !== 4) {
    return "";
  }

  for (var index = 0; index < parts.length; index++) {
    var number = parseInt(parts[index], 10);
    if (isNaN(number) || number < 0 || number > 255) {
      return "";
    }
  }

  return candidate;
}

function parsePortList(csv) {
  var list = [];
  if (!csv) {
    return list;
  }

  var parts = ("" + csv).split(",");
  for (var index = 0; index < parts.length; index++) {
    var port = parsePort(parts[index]);
    if (port > 0) {
      list.push(port);
    }
  }

  return list;
}

function isPortAvailable(port) {
  var shell = new ActiveXObject("WScript.Shell");
  var fso = new ActiveXObject("Scripting.FileSystemObject");
  var tempFolder = shell.ExpandEnvironmentStrings("%TEMP%\\");
  var outputPath = tempFolder + "gamebot_portscan_" + port + "_" + (new Date().getTime()) + ".txt";
  var command = "%ComSpec% /c netstat -ano -p tcp > \"" + outputPath + "\"";
  shell.Run(command, 0, true);

  if (!fso.FileExists(outputPath)) {
    return true;
  }

  var file = fso.OpenTextFile(outputPath, 1, false);
  var output = file.ReadAll();
  file.Close();
  try {
    fso.DeleteFile(outputPath, true);
  }
  catch (ignored) {
  }

  return output.indexOf(":" + port + " ") === -1;
}

function firstAvailable(candidates) {
  for (var index = 0; index < candidates.length; index++) {
    if (isPortAvailable(candidates[index])) {
      return candidates[index];
    }
  }

  return -1;
}

function uniquePush(list, value) {
  for (var index = 0; index < list.length; index++) {
    if (list[index] === value) {
      return;
    }
  }

  list.push(value);
}

function addInterfaceCandidate(candidates, interfaceName, ipAddress) {
  var name = trimText(interfaceName);
  var ip = parseIpv4(ipAddress);
  if (ip.length === 0 || ip === "127.0.0.1" || ip === "0.0.0.0") {
    return;
  }

  for (var index = 0; index < candidates.length; index++) {
    if (candidates[index].ip === ip) {
      return;
    }
  }

  candidates.push({
    name: name.length > 0 ? name : "interface",
    ip: ip
  });
}

function discoverInterfaceCandidatesPowerShell() {
  var shell = new ActiveXObject("WScript.Shell");
  var fso = new ActiveXObject("Scripting.FileSystemObject");
  var tempFolder = shell.ExpandEnvironmentStrings("%TEMP%\\");
  var scriptPath = tempFolder + "gamebot_interfaces_" + (new Date().getTime()) + ".ps1";
  var outputPath = tempFolder + "gamebot_interfaces_ps_" + (new Date().getTime()) + ".txt";
  var scriptContent = "Get-NetIPAddress -AddressFamily IPv4 | "
    + "Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -ne '0.0.0.0' -and $_.PrefixOrigin -ne 'WellKnown' } | "
    + "Sort-Object InterfaceAlias,IPAddress | "
    + "ForEach-Object { \"$($_.InterfaceAlias)|$($_.IPAddress)\" }";
  var command = "%ComSpec% /c powershell -NoProfile -ExecutionPolicy Bypass -File \""
    + scriptPath
    + "\" > \""
    + outputPath
    + "\"";

  var candidates = [];
  var scriptFile = null;
  try {
    scriptFile = fso.OpenTextFile(scriptPath, 2, true);
    scriptFile.Write(scriptContent);
    scriptFile.Close();
    shell.Run(command, 0, true);
  }
  catch (ignored) {
    appendDebugLog("PowerShell discovery execution failed.");
  }
  finally {
    try {
      if (scriptFile !== null) {
        scriptFile.Close();
      }
    }
    catch (ignoredClose) {
    }

    try {
      if (fso.FileExists(scriptPath)) {
        fso.DeleteFile(scriptPath, true);
      }
    }
    catch (ignoredDelete) {
    }
  }

  if (!fso.FileExists(outputPath)) {
    return candidates;
  }

  var text = "";
  try {
    var file = fso.OpenTextFile(outputPath, 1, false);
    text = file.ReadAll();
    file.Close();
  }
  finally {
    try {
      fso.DeleteFile(outputPath, true);
    }
    catch (ignored) {
    }
  }

  var lines = text.split(/\r?\n/);
  for (var index = 0; index < lines.length; index++) {
    var line = trimText(lines[index]);
    if (line.length === 0 || line.indexOf("|") < 0) {
      continue;
    }

    var parts = line.split("|");
    if (parts.length < 2) {
      continue;
    }

    addInterfaceCandidate(candidates, parts[0], parts[1]);
  }

  return candidates;
}

function discoverInterfaceCandidates() {
  var fromPowerShell = discoverInterfaceCandidatesPowerShell();
  if (fromPowerShell.length > 0) {
    appendDebugLog("PowerShell discovery returned " + fromPowerShell.length + " interface(s).");
    return fromPowerShell;
  }

  appendDebugLog("PowerShell discovery returned 0; falling back to ipconfig parser.");

  var shell = new ActiveXObject("WScript.Shell");
  var fso = new ActiveXObject("Scripting.FileSystemObject");
  var tempFolder = shell.ExpandEnvironmentStrings("%TEMP%\\");
  var outputPath = tempFolder + "gamebot_interfaces_" + (new Date().getTime()) + ".txt";
  var command = "%ComSpec% /c ipconfig > \"" + outputPath + "\"";
  var candidates = [];

  shell.Run(command, 0, true);

  if (!fso.FileExists(outputPath)) {
    return candidates;
  }

  var text = "";
  try {
    var file = fso.OpenTextFile(outputPath, 1, false);
    text = file.ReadAll();
    file.Close();
  }
  finally {
    try {
      fso.DeleteFile(outputPath, true);
    }
    catch (ignored) {
    }
  }

  var lines = text.split(/\r?\n/);
  var currentInterface = "";
  for (var lineIndex = 0; lineIndex < lines.length; lineIndex++) {
    var line = lines[lineIndex] || "";
    var trimmed = trimText(line);

    if (trimmed.length === 0) {
      continue;
    }

    if (/^[^\s].*:\s*$/.test(line)) {
      currentInterface = trimText(trimmed.substring(0, trimmed.length - 1));
      continue;
    }

    if (!currentInterface) {
      continue;
    }

    if (line.indexOf(":") >= 0) {
      addInterfaceCandidate(candidates, currentInterface, line);
    }
  }

  appendDebugLog("ipconfig discovery returned " + candidates.length + " interface(s).");

  return candidates;
}

function sqlEscape(value) {
  return ("" + value).replace(/'/g, "''");
}

function describeError(errorObject) {
  if (!errorObject) {
    return "unknown";
  }

  var number = "";
  var description = "";
  try {
    number = errorObject.number;
  }
  catch (ignoredNumber) {
  }

  try {
    description = errorObject.description;
  }
  catch (ignoredDescription) {
  }

  return "number=" + number + ", description=" + description;
}

function insertComboRow(propertyName, order, value, text) {
  var sql = "SELECT `Property`,`Order`,`Value`,`Text` FROM `ComboBox`";
  var view = null;
  try {
    view = Session.Database.OpenView(sql);
    view.Execute();

    var record = Session.Installer.CreateRecord(4);
    record.StringData(1) = propertyName;
    record.IntegerData(2) = order;
    record.StringData(3) = value;
    record.StringData(4) = text;

    view.Modify(7, record);
  }
  finally {
    if (view !== null) {
      view.Close();
    }
  }
}

function discoverAndPopulateInterfaces() {
  var propertyName = "BIND_HOST";
  if (trimText(Session.Property("BIND_HOST_DISCOVERY_POPULATED")) === "1") {
    appendDebugLog("Discovery rows already populated for this session; skipping.");
    return;
  }

  var discovered = discoverInterfaceCandidates();
  appendDebugLog("Populating ComboBox with " + discovered.length + " discovered interface row(s).");
  var order = 3;
  for (var index = 0; index < discovered.length; index++) {
    var item = discovered[index];
    var text = item.name + " (" + item.ip + ")";
    appendDebugLog("Adding item: " + text);
    try {
      insertComboRow(propertyName, order, item.ip, text);
    }
    catch (insertErr) {
      appendDebugLog("Insert failed for item: " + text + " | " + describeError(insertErr));
    }
    order++;
  }

  Session.Property("BIND_HOST_DISCOVERY_POPULATED") = "1";
}

function updateShortcutHost() {
  var bindHost = trimText(Session.Property("BIND_HOST"));
  if (bindHost === "127.0.0.1" || bindHost.length === 0) {
    Session.Property("SHORTCUT_HOST") = "localhost";
    return;
  }

  var computerName = trimText(Session.Property("ComputerName"));
  if (computerName.length > 0) {
    Session.Property("SHORTCUT_HOST") = computerName;
    return;
  }

  Session.Property("SHORTCUT_HOST") = bindHost;
}

function resolvePort(requestedRaw, fallbackList) {
  var candidates = [];
  var requested = parsePort(requestedRaw);

  if (requested > 0) {
    uniquePush(candidates, requested);
  }

  for (var index = 0; index < fallbackList.length; index++) {
    uniquePush(candidates, fallbackList[index]);
  }

  var resolved = firstAvailable(candidates);
  if (resolved > 0) {
    return resolved;
  }

  return requested > 0 ? requested : fallbackList[0];
}

function DetectAvailablePorts() {
  var requestedPort = Session.Property("PORT");
  var preferredPortOrder = Session.Property("PREFERRED_PORT_ORDER");

  var portFallback = parsePortList(preferredPortOrder);
  if (portFallback.length === 0) {
    portFallback = [8080, 8088, 8888, 80];
  }

  var resolvedPort = resolvePort(requestedPort, portFallback);

  Session.Property("PORT") = "" + resolvedPort;
  updateShortcutHost();

  return 1;
}

function DiscoverBindInterfaces() {
  var currentBindHost = trimText(Session.Property("BIND_HOST"));
  if (currentBindHost.length === 0) {
    Session.Property("BIND_HOST") = "127.0.0.1";
    appendDebugLog("BIND_HOST was empty; defaulted to 127.0.0.1.");
  }

  discoverAndPopulateInterfaces();
  updateShortcutHost();

  return 1;
}
