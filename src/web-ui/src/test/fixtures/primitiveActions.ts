export type PrimitiveFixturePayload = {
  type: string;
  schemaVersion: 'v1';
  payload: Record<string, unknown>;
};

export const primitiveTapFixture: PrimitiveFixturePayload = {
  type: 'tap',
  schemaVersion: 'v1',
  payload: {
    referenceImageId: 'home_button',
    confidence: 0.95
  }
};

export const primitiveCommandFixture: PrimitiveFixturePayload = {
  type: 'command',
  schemaVersion: 'v1',
  payload: {
    commandId: 'cmd-nested-001'
  }
};

export const primitiveConnectFixture: PrimitiveFixturePayload = {
  type: 'connect-to-game',
  schemaVersion: 'v1',
  payload: {
    gameId: 'game-001',
    adbSerial: 'emulator-5554'
  }
};
