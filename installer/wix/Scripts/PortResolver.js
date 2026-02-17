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
  var requestedPort = Session.Property("WEB_PORT");
  var preferredWebOrder = Session.Property("PREFERRED_WEB_PORT_ORDER");

  var portFallback = parsePortList(preferredWebOrder);
  if (portFallback.length === 0) {
    portFallback = [8080, 8088, 8888, 80];
  }

  var resolvedPort = resolvePort(requestedPort, portFallback);

  Session.Property("BACKEND_PORT") = "" + resolvedPort;
  Session.Property("WEB_PORT") = "" + resolvedPort;

  return 1;
}
