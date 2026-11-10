# Feature Specification: Single-Owner Sign-In for Internet Access

**Feature Branch**: `187-single-user-auth`  
**Created**: 2026-11-10  
**Status**: Draft  
**Input**: User description: "I want to expose the web service over the internet, so I need a proper authentication. I explicitly don't need authorization. It needs to be secure, but as I don't need to let more users, keep it simple. Most likely I will be the only user. Authentication has to be secure and simple, I was thinking via Google or something similar, as I don't want to mess with passwords. Make me couple of suggestions. Main thing is I want simplicity from my phone and making sure nobody else can log in or register new account from the internet"

## Clarifications

### Session 2026-11-10

- Q: Which sign-in method does the owner want? → A: Passkey on the device.
- Q: Does the existing access token for automated clients stay valid from the internet? → A: No. The service accepts the token only from the machine where the service runs. (Changed by the later answer about home network devices: the home network is also permitted.)
- Q: How does the owner get a registration code? → A: With a local command on the service machine only. A signed-in device cannot make a code.
- Q: What is the default state of the sign-in function? → A: Off by default. While it is off, the service refuses each request that does not start on the service machine or the home network.
- Q: Where does the gateway run? → A: It can run on a different host. The service machine itself is not exposed to the internet. A reverse proxy (for example Traefik or nginx) on a different host of the home network is exposed and forwards the requests.
- Q: How does a home network device get access when the sign-in function is on? → A: It can use the public address with a passkey, or the direct address with the access token. The service accepts the token from the service machine and from the home network, and never from the internet.
- Q: Is the connection between the proxy and the service encrypted? → A: No. Plain HTTP on this hop is accepted. The guide recommends a firewall rule for the service port.
- Q: How does the service find a gateway on the service machine that adds no forward information? → A: The owner configures the address of the service machine itself as a trusted proxy.
- Q: How does the service stop one source that sends many requests to start a sign-in? → A: With a limit of open sign-in and registration requests for each source.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The owner signs in from a phone (Priority: P1)

The owner opens the web UI on a phone from the internet. The service shows a sign-in page. The owner signs in with a passkey on the phone. The phone confirms the owner with its screen lock (fingerprint, face, or PIN). The owner does not type a password that the service keeps. After the sign-in, the owner sees the page that the owner requested.

**Why this priority**: This is the reason for the feature. Without a sign-in, the owner cannot safely put the service on the internet.

**Independent Test**: Open the web UI from a phone on a mobile network. Sign in as the owner. Make sure that the requested page opens and that all functions work.

**Acceptance Scenarios**:

1. **Given** the owner is not signed in, **When** the owner opens a page of the web UI, **Then** the service shows the sign-in page and shows no application data.
2. **Given** the sign-in page is open on a phone, **When** the owner completes the sign-in, **Then** the service opens the page that the owner first requested.
3. **Given** the owner is signed in, **When** the owner uses a function of the web UI, **Then** the function works the same as before this feature.
4. **Given** the owner is on the sign-in page, **When** the owner looks at the page, **Then** the page has no password field and no registration link.
5. **Given** the owner signed in on a device, **When** the owner opens a different page or loads the page again, **Then** the service does not ask for a sign-in.

---

### User Story 2 - The service refuses all other persons (Priority: P1)

A person who is not the owner finds the service on the internet. The person cannot sign in. The person cannot make an account. The person cannot read data or start an action. The service gives the same refusal for each cause, so the person learns nothing about the owner.

**Why this priority**: The service controls devices and holds game data. Access by a different person is the primary risk of internet exposure.

**Independent Test**: Try to sign in with a passkey that is not registered. Try to register a passkey with no registration code. Send requests without a sign-in to the pages and to the API. Make sure that each attempt gets a refusal and no data.

**Acceptance Scenarios**:

1. **Given** a person who is not the owner, **When** the person tries to sign in with a passkey that is not registered, **Then** the service refuses access and registers nothing.
2. **Given** a request without a valid session, **When** the request goes to an API endpoint, **Then** the service refuses the request and returns no application data.
3. **Given** a request without a valid session, **When** the request tries to start, stop, or change something, **Then** the service does nothing and refuses the request.
4. **Given** a person on the internet with no valid registration code, **When** the person tries to register a passkey, **Then** the service refuses the attempt.
5. **Given** many failed sign-in attempts from one source in a short time, **When** the next attempt arrives, **Then** the service slows or blocks that source for a time.
6. **Given** the sign-in function is off, **When** a request arrives from the internet, **Then** the service refuses the request and returns no application data.
7. **Given** the sign-in function is off, **When** a request arrives from the service machine or the home network, **Then** the service works the same as before this feature.

---

### User Story 3 - The owner registers a device (Priority: P1)

The owner wants to use a new phone or a new computer. On the machine where the service runs, the owner runs a local command. The command prints a one-time registration code and a registration link. On the new device, the owner opens the registration page, enters the code, and makes a passkey. From then on, that device can sign in.

**Why this priority**: No device can sign in before it has a registered passkey. This flow is also the only way to add a device, so it must be safe.

**Independent Test**: Make a registration code on the service machine. Use it on a phone to register a passkey. Sign in with the passkey. Then try the same code again and make sure that the service refuses it.

**Acceptance Scenarios**:

1. **Given** the owner is at the machine where the service runs, **When** the owner runs the local registration command, **Then** the command prints a one-time code and a registration link, and the code stays valid for 10 minutes.
2. **Given** a valid registration code, **When** the owner enters it on a new device and makes a passkey, **Then** the service registers the passkey and signs the device in.
3. **Given** a registration code that was used or that expired, **When** a person enters it, **Then** the service refuses the registration.
4. **Given** a web request from any source, with or without a valid session, **When** the request asks for a registration code, **Then** the service gives no code.
5. **Given** the owner is at the machine where the service runs, **When** the owner removes a passkey with the local command, **Then** that passkey cannot sign in and its sessions end.

---

### User Story 4 - The owner uses a proxy on a different host (Priority: P1)

The service machine is not exposed to the internet. A reverse proxy on a different host of the home network is exposed. It has the public address and the encrypted connection, and it forwards the requests to the service. The owner tells the service the address of the proxy. From then on, the service treats each request from the proxy as a request from the internet. All sign-in functions work through the proxy.

**Why this priority**: This is the network layout of the owner. Without it, the service sees a request from the proxy as a request from a home network device. Then the protection does not work, or the sign-in does not work.

**Independent Test**: Put a proxy on a second host. Configure its address in the service. Do the registration and the sign-in through the public address. Then send requests through the proxy with no session and make sure that each one gets a refusal.

**Acceptance Scenarios**:

1. **Given** the proxy address is configured and the sign-in function is on, **When** the owner registers a device and signs in through the public address, **Then** the two flows work as in User Stories 1 and 3.
2. **Given** the proxy address is configured and the sign-in function is on, **When** a request with a correct access token and no session arrives from the proxy, **Then** the service refuses the request.
3. **Given** the proxy address is configured and the sign-in function is off, **When** a request arrives from the proxy, **Then** the service refuses the request, also when the proxy adds no forward information.
4. **Given** the proxy address is configured, **When** the proxy changes the host name of the request to the address of the service machine, **Then** the registration and the sign-in continue to work.
5. **Given** the proxy sends the address of the original client, **When** a person makes many failed attempts, **Then** the service blocks that client and not all clients of the proxy.
6. **Given** the sign-in function is on, **When** a device on the home network opens the service directly (not through the public address) with no access token, **Then** the service gives no data, and the page shows the access token prompt and the public address.
7. **Given** the sign-in function is on and an access token is configured, **When** a device on the home network opens the service directly with the correct access token, **Then** the service gives access.
8. **Given** the sign-in function is on and no access token is configured, **When** a device on the home network opens the service directly, **Then** the service gives no access, and the page shows only the public address.
9. **Given** a request that does not come from the configured proxy, **When** the request contains a forged client address, **Then** the service ignores the forged address.

---

### User Story 5 - The owner stays signed in on a trusted device (Priority: P2)

The owner signs in one time on the phone. On the subsequent days, the owner opens the web UI and it works immediately. The owner does not sign in again each day. User Story 1 gives the session. This story gives its length and its life after a restart.

**Why this priority**: The owner wants simple use from the phone. A sign-in for each visit makes the phone use slow.

**Independent Test**: Sign in on a phone. Close the browser. Open the web UI on the next day. Make sure that no sign-in is necessary.

**Acceptance Scenarios**:

1. **Given** the owner signed in on a device less than 30 days ago, **When** the owner opens the web UI on that device, **Then** the service does not ask for a sign-in.
2. **Given** the owner uses the web UI regularly, **When** each visit occurs, **Then** the service extends the session by 30 days from that visit.
3. **Given** a session that had no use for 30 days, **When** the owner opens the web UI, **Then** the service asks for a sign-in.
4. **Given** the service restarts, **When** the owner opens the web UI on a device with a valid session, **Then** the session stays valid.

---

### User Story 6 - The owner signs out and manages devices (Priority: P3)

The owner can sign out on the current device. The owner can also end all sessions on all devices, for example after the loss of a phone. In the web UI, the owner can see the registered devices, give each a name, and remove one.

**Why this priority**: This limits the damage if a device goes to a different person. The primary flows work without it, because the local command can remove a passkey and end the sessions.

**Independent Test**: Sign in on two devices. End all sessions from one device. Make sure that the two devices must sign in again. Then remove one passkey in the list and make sure that it cannot sign in.

**Acceptance Scenarios**:

1. **Given** the owner is signed in, **When** the owner selects the sign-out control, **Then** the session on that device ends and the sign-in page shows.
2. **Given** the owner is signed in on two devices, **When** the owner ends all sessions, **Then** each device must sign in again.
3. **Given** the owner is signed in, **When** the owner opens the list of registered devices, **Then** the owner sees each passkey with its name, its registration date, and its last use.
4. **Given** the list of registered devices, **When** the owner removes a passkey, **Then** that passkey cannot sign in and its sessions end.

---

### Edge Cases

- The session expires while a page is open. The next action sends the owner to the sign-in page. After the sign-in, the owner returns to the same page.
- No passkey is registered, but the sign-in function is on. The service refuses all sign-in attempts. It does not let the first visitor become the owner.
- The device or the browser does not support passkeys. The sign-in page shows a clear message. The service gives no password fallback.
- The owner loses the only device that can sign in. The owner makes a new registration code on the machine where the service runs, registers a new device, and removes the lost passkey.
- The public address of the service changes. Passkeys for the old address do not work at the new address. The owner must register the devices again.
- A person tries many registration codes. The limit on failed attempts (FR-016) also applies to registration codes.
- The owner makes a second registration code before the first one expires. Only the newest code is valid.
- A page is open when the session ends. The next data request of the page gets the usual refusal, and the page shows the sign-in page.
- One source sends many requests to start a sign-in. The limit for each source (FR-016) keeps these requests from the sign-in of the owner.
- A gateway on the service machine adds no forward information. The owner configures the address of the service machine itself as a trusted proxy. Then each request from the service machine is an internet request, and the access token works only on the home network.
- The owner exposes the service to the internet and forgets to set the sign-in function on. The service refuses all requests from the internet. Local use continues to work.
- The proxy on a different host adds no forward information, and the owner did not configure the proxy address. The service cannot tell this request from a request of a home network device. The setup guide makes the proxy address a mandatory setting and has a check that finds this error.
- The proxy sends no address of the original client. All internet clients then share one limit on failed attempts. The service writes a warning to the record one time.
- The address of the proxy changes (for example a new container address). Requests from the new address are not from a configured proxy. The owner can configure an address range.
- A request arrives over a connection that is not encrypted. The service does not send a session secret over that connection.
- An automated client on the service machine or on the home network uses the existing access token. The request works with no passkey. See FR-015.
- A home network device sends wrong access tokens many times. The limit on failed attempts (FR-016) blocks that device for a time.
- A request from the internet has a correct access token and no valid session. The service refuses the request with the usual refusal.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The service MUST require a valid sign-in for all pages and all API endpoints, when the sign-in function is on. This includes the API documentation pages.
- **FR-002**: The service MUST keep the health check endpoint open without a sign-in. The health check MUST show no application data.
- **FR-003**: The service MUST let the owner sign in with a passkey that is registered with the service. The device MUST confirm the owner with its screen lock.
- **FR-004**: The service MUST NOT keep a password for the owner and MUST NOT show a password field. The service MUST NOT give a password fallback.
- **FR-005**: The service MUST accept only registered passkeys. The owner MUST be able to register more than one passkey, one for each device.
- **FR-006**: The service MUST register a passkey only with a valid one-time registration code. The service MUST have no other registration function.
- **FR-007**: The owner MUST get a registration code only from a local command on the machine where the service runs. The command MUST print the code and a registration link. No page and no API may give a registration code, also not to a signed-in device.
- **FR-008**: The service MUST refuse all sign-in attempts when no passkey is registered (fail closed).
- **FR-009**: The service MUST give all registered passkeys the same full access. This feature adds no roles and no permissions.
- **FR-010**: A registration code MUST work one time only and MUST expire 10 minutes after the service makes it. A new code MUST cancel the previous code.
- **FR-011**: A signed-in owner MUST be able to see the registered passkeys, give each a name, and remove a passkey. When the owner removes a passkey, its sessions MUST end.
- **FR-012**: The owner MUST be able to list and remove passkeys with a local command on the machine where the service runs, without a sign-in. This is the recovery path for a lost device.
- **FR-013**: The service MUST keep a session on a device for 30 days after the last use. The session MUST stay valid after a service restart.
- **FR-014**: The owner MUST be able to sign out on the current device and to end all sessions on all devices.
- **FR-015**: When the sign-in function is on, the service MUST accept the existing access token only on a request that starts on the machine where the service runs or on the home network. The service MUST refuse the token on a request that arrives through the public address, also when a gateway on the same machine or a proxy on a different host forwards that request. When no token is configured, token access MUST be off.
- **FR-016**: The service MUST limit repeated failed sign-in attempts from one source. When the sign-in function is on, a request with a wrong access token is also a failed attempt. The service MUST also limit the number of open sign-in and registration requests of one source, so that one source cannot stop the sign-in of the owner.
- **FR-017**: The service MUST record each sign-in, each failed sign-in, and each sign-out with the time and the source. The record MUST NOT contain secrets.
- **FR-018**: The refusal message MUST be the same for each cause. It MUST NOT show which passkeys are registered or if a registration code exists.
- **FR-019**: The browser MUST get and send the session secret only over an encrypted connection to the public address. Scripts on a page MUST NOT be able to read the session secret. The hop between the proxy and the service is in the home network and can be not encrypted (see Assumptions).
- **FR-020**: The service MUST protect signed-in sessions from requests that a different web site makes in the name of the owner.
- **FR-021**: After the sign-in, the service MUST return the owner to the page that the owner first requested. The service MUST NOT redirect to a different web site.
- **FR-022**: The owner MUST be able to set the sign-in function on or off in the service configuration. The default MUST be off. The documentation MUST tell the owner to set it on before internet exposure.
- **FR-023**: While the sign-in function is off, the service MUST refuse each request that does not start on the service machine or the home network. This includes a request from the internet that a gateway on the same machine or a configured proxy on a different host forwards. The health check endpoint stays open (FR-002).
- **FR-024**: When the service refuses a request because of FR-023, the service MUST record the refusal. The record MUST tell the owner to set the sign-in function on.
- **FR-025**: The sign-in page, the registration page, the list of registered devices, and the sign-out control MUST have full phone support and MUST follow docs/mobile-ui-principles.md.
- **FR-026**: The documentation MUST give the setup steps and the recovery steps for a lost device.
- **FR-027**: The owner MUST be able to configure the addresses, or address ranges, of the trusted proxies in the service configuration. The owner MUST be able to include the address of the service machine itself, for a gateway on the service machine that adds no forward information. Only the service machine can change the sign-in settings (on or off, public address, trusted proxies, access token). No page and no API may change them.
- **FR-028**: The service MUST treat each request from a configured proxy as a request from the internet. This rule MUST NOT depend on forward information in the request.
- **FR-029**: The service MUST use the client address that a request gives only when the request comes from a configured proxy or from a gateway on the service machine. For all other requests, the service MUST ignore that address. The limit on failed attempts (FR-016) and the sign-in record (FR-017) use the client address.
- **FR-030**: The registration and the sign-in MUST work when the proxy keeps the public host name and when the proxy changes it. The service MUST use the configured public address for its checks, not the host name of the request.
- **FR-031**: When the sign-in function is on and a home network device opens the service directly with no access, the page MUST show the public address to use. The page MUST also show the access token prompt when an access token is configured. The page MUST show no application data.
- **FR-032**: The documentation MUST give tested examples for a proxy on a different host (Traefik and nginx), the mandatory proxy address setting, a firewall recommendation for the service machine, and a check that proves the setup.

### Key Entities

- **Passkey**: The registered proof for one device of the owner. It has a name, a registration date, and a last-use time. It holds no secret that lets a person sign in. The service keeps at most 20 passkeys.
- **Registration code**: A one-time code that permits the registration of one passkey. The service machine makes it. It has an expiry.
- **Session**: The proof that a device signed in. It belongs to one passkey. It has a start time, a last-use time, and an expiry. The owner can end it.
- **Sign-in record**: One entry for each sign-in, failed sign-in, and sign-out, and for each registration, passkey removal, blocked source, and refused internet request. It has the time, the source, and the result.
- **Trusted proxy**: An address or an address range of a host that forwards internet requests to the service. The owner configures it. The service has no trust in the requests from it; the name tells only that the service believes its forward information.
- **Access token**: The existing secret for automated clients. It stays optional. With the sign-in function on, it works only on the service machine and on the home network.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The owner completes a sign-in on a registered phone in less than 15 seconds, with no password typed into the service.
- **SC-002**: The owner registers a new device in less than 2 minutes, from the request for the code to the first sign-in.
- **SC-003**: 100% of registration attempts with no code, a used code, or an expired code get a refusal, and the number of registered passkeys does not change.
- **SC-004**: On a device with a valid session, the owner gets to the web UI with zero sign-in steps.
- **SC-005**: 100% of requests without a valid sign-in to protected pages and endpoints get a refusal and no application data.
- **SC-006**: 100% of sign-in attempts with a passkey that is not registered get a refusal.
- **SC-007**: After the owner ends all sessions, each device must sign in again in less than 1 minute.
- **SC-008**: With the sign-in function off, 100% of requests from the internet get a refusal and no application data.
- **SC-009**: With a proxy on a different host and its address configured, 100% of the acceptance scenarios of the other user stories pass through the public address.
- **SC-010**: With the proxy address configured, 100% of requests from the proxy with no valid session get a refusal, with and without forward information, in the two states of the sign-in function.
- **SC-011**: All functions that worked before this feature work the same for a signed-in owner.
- **SC-012**: The owner can do the full setup from the documentation in less than 30 minutes.

## Assumptions

- The owner is the only user. The service has no user accounts. All passkeys belong to the owner.
- The owner selected the passkey method. Google sign-in and an access gateway were the alternatives. They are out of scope.
- Authorization is out of scope. All registered passkeys have full access.
- The service has a stable public domain name. A passkey works only at the address where the owner registered it.
- The devices of the owner support passkeys. Current phones and current browsers do.
- The owner supplies the internet exposure itself (public address, encrypted connection, routing). This feature does not make the network path. The documentation gives recommendations.
- The primary layout is a reverse proxy (Traefik or nginx) on a different host of the home network. The service machine is not exposed to the internet. A gateway on the service machine is also supported.
- The home network between the proxy and the service machine is trusted. The connection on this hop can be plain HTTP. A person who can read this traffic can also read the traffic of all current home network use.
- The service listens on a home network address, so that the proxy can connect. The documentation recommends a firewall rule that permits the service port only from the proxy host and from the home network devices of the owner. HTTPS on the service itself is out of scope.
- With the sign-in function on, a device on the home network has two ways in: the public address with a passkey, or the direct address with the access token. The token goes over plain HTTP in the home network, as it does today.
- The token rule for the home network is safe only when the service knows the proxy. A proxy on a different host that is not configured and that adds no forward information looks like a home network device. Then a person on the internet who has the token gets access. The proxy address setting (FR-027) and the setup check (FR-032) prevent this.
- The internet address uses an encrypted connection. The sign-in function does not work safely without it.
- The sign-in function is off by default, so current local installations do not change. The owner sets it on before internet exposure. If the owner forgets, the service refuses requests from the internet (FR-023).
- "Home network" means the private network addresses of the service machine. With the sign-in function off, devices on that network have access with no sign-in, as today.
- A request from the same machine with no access token gets no special trust when the sign-in function is on. A gateway on the same machine can make internet requests look local. For such a gateway, the owner configures the address of the service machine itself as a trusted proxy (FR-027). For the same reason, the owner makes a registration code with a local command on the service machine, not through a web request.
- The owner can get to the service machine (directly or by remote desktop) to add a device.
- The session length is 30 days after the last use. This balances phone simplicity and risk for one owner.
- Automated clients (scripts, tests) run on the service machine or on the home network and continue to use the existing access token there.
- Recovery after the loss of all sign-in devices occurs on the machine where the service runs.
