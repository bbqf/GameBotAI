// CommonJS jest config: with "type": "module" in package.json, node >= 23 refuses jest's
// require() of a jest.config.ts, so the config lives in .cjs form (identical options).
/** @type {import('jest').Config} */
const config = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  moduleFileExtensions: ['ts', 'tsx', 'js'],
  setupFilesAfterEnv: ['<rootDir>/src/setupTests.ts'],
  moduleNameMapper: {
    '\\.(css|less|sass|scss)$': '<rootDir>/src/testUtils/styleMock.ts'
  },
  collectCoverage: true,
  collectCoverageFrom: ['<rootDir>/src/**/*.{ts,tsx}', '!<rootDir>/src/**/*.d.ts', '!<rootDir>/src/**/__tests__/**'],
  coverageThreshold: {
    global: {
      lines: 40,
      branches: 50,
      functions: 0,
      statements: 0
    },
    './src/components/images/EmulatorCaptureCropper.tsx': {
      lines: 80,
      branches: 60,
      functions: 80,
      statements: 80
    }
  }
};

module.exports = config;
