const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const frontend = path.resolve(__dirname, '../../src/Desarrollo.Web/ClientApp');
const ts = require(path.join(frontend, 'node_modules/typescript'));
require.extensions['.ts'] = (module, filename) => {
  const output = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } });
  module._compile(output.outputText, filename);
};
const { createAlertSound } = require(path.join(frontend, 'src/live/sound.ts'));

function audioFixture() {
  const instances = [];
  class AudioContextFake {
    constructor() {
      this.state = 'suspended'; this.currentTime = 0; this.destination = {};
      this.oscillators = []; this.gains = []; this.resumeCalls = 0; this.closeCalls = 0;
      instances.push(this);
    }
    async resume() { this.resumeCalls++; if (this.rejectResume) throw new Error('Gesture required'); this.state = 'running'; }
    async close() { this.closeCalls++; this.state = 'closed'; if (this.rejectClose) throw new Error('Device closed'); }
    createOscillator() {
      const oscillator = { type: null, frequency: { setValueAtTime(value, at) { oscillator.frequencyValue = value; oscillator.frequencyAt = at; } },
        connect(target) { this.target = target; }, disconnect() { this.disconnected = true; },
        start(at) { this.startedAt = at; }, stop(at) { this.stoppedAt = at; this.stopCalls = (this.stopCalls || 0) + 1; }, onended: null };
      this.oscillators.push(oscillator); return oscillator;
    }
    createGain() {
      const events = []; const gain = { gain: {
        setValueAtTime(value, at) { events.push({ kind: 'set', value, at }); },
        linearRampToValueAtTime(value, at) { events.push({ kind: 'linear', value, at }); },
        exponentialRampToValueAtTime(value, at) { events.push({ kind: 'exponential', value, at }); },
      }, connect(target) { this.target = target; }, disconnect() { this.disconnected = true; }, events };
      this.gains.push(gain); return gain;
    }
  }
  global.window = { AudioContext: AudioContextFake };
  return { instances, AudioContextFake };
}

test('Sound never creates audio before explicit unlock; unavailable and rejected audio can retry safely', async () => {
  global.window = {};
  const absent = createAlertSound();
  assert.doesNotThrow(() => absent.play('message'));
  assert.equal(await absent.unlock(), false);
  absent.close();

  const { instances } = audioFixture(); const alerts = createAlertSound();
  alerts.play('message'); assert.equal(instances.length, 0);
  const pending = alerts.unlock(); assert.equal(instances.length, 1);
  assert.equal(await pending, true); assert.equal(instances[0].resumeCalls, 1);
  assert.equal(instances[0].oscillators.length, 0, 'unlock does not make an unsolicited sound');
  instances[0].state = 'suspended'; instances[0].rejectResume = true;
  assert.equal(await alerts.unlock(), false);
  alerts.play('notification'); assert.equal(instances[0].oscillators.length, 0);
  instances[0].rejectResume = false;
  assert.equal(await alerts.unlock(), true);
  assert.equal(instances.length, 1, 'retry reuses the existing context');
  alerts.play('notification'); assert(instances[0].oscillators.length > 0);
  alerts.close();
});

test('Concurrent unlocks share a resume, and close while resuming cannot reopen playback', async () => {
  const { instances, AudioContextFake } = audioFixture(); let finishResume;
  AudioContextFake.prototype.resume = function () {
    this.resumeCalls++;
    return new Promise((resolve) => { finishResume = () => { this.state = 'running'; resolve(); }; });
  };
  const alerts = createAlertSound(); const first = alerts.unlock(); const second = alerts.unlock();
  assert.strictEqual(first, second); assert.equal(instances[0].resumeCalls, 1);
  alerts.close(); finishResume();
  assert.equal(await first, false); assert.equal(await second, false);
  alerts.play('message'); assert.equal(instances[0].oscillators.length, 0);
  assert.equal(await alerts.unlock(), false); assert.equal(instances.length, 1);
});

test('Incoming alert types have distinct quiet tones and rapid events produce one alert', async () => {
  const { instances } = audioFixture(); const alerts = createAlertSound();
  await alerts.unlock(); const context = instances[0];
  alerts.play('message'); const messageTones = context.oscillators.map((tone) => tone.frequencyValue);
  assert(messageTones.length > 0);
  assert(context.gains.every((gain) => gain.events.find((event) => event.kind === 'linear').value <= 0.05));
  assert(context.oscillators.every((tone) => tone.stoppedAt > tone.startedAt && tone.stoppedAt - tone.startedAt < 0.5));
  context.currentTime = 0.69; alerts.play('notification'); assert.equal(context.oscillators.length, messageTones.length);
  context.currentTime = 0.71; alerts.play('notification');
  const notificationTones = context.oscillators.slice(messageTones.length).map((tone) => tone.frequencyValue);
  assert.notDeepEqual(notificationTones, messageTones);
  context.state = 'suspended'; context.currentTime = 1.5;
  alerts.play('message'); assert.equal(context.oscillators.length, messageTones.length + notificationTones.length);
  alerts.close();
});

test('Finished tones and close disconnect nodes; close rejection cannot escape or allow later sounds', async () => {
  const { instances } = audioFixture(); const alerts = createAlertSound();
  await alerts.unlock(); const context = instances[0];
  alerts.play('message'); const first = context.oscillators[0]; first.onended();
  assert.equal(first.disconnected, true); assert.equal(context.gains[0].disconnected, true);
  context.rejectClose = true;
  assert.doesNotThrow(() => alerts.close()); await new Promise((resolve) => setImmediate(resolve));
  assert(context.oscillators.every((tone) => tone.disconnected)); assert(context.gains.every((gain) => gain.disconnected));
  const previous = context.oscillators.length;
  alerts.play('message'); alerts.close();
  assert.equal(await alerts.unlock(), false); assert.equal(context.oscillators.length, previous);
  assert.equal(context.closeCalls, 1);
});

test('An audio node failure stays optional and releases any partially scheduled tones', async () => {
  const { instances } = audioFixture(); const alerts = createAlertSound();
  await alerts.unlock(); const context = instances[0];
  const create = context.createOscillator.bind(context); let calls = 0;
  context.createOscillator = () => { if (++calls === 2) throw new Error('Audio device unavailable'); return create(); };
  assert.doesNotThrow(() => alerts.play('notification'));
  assert.equal(context.oscillators[0].disconnected, true); assert.equal(context.gains[0].disconnected, true);
  alerts.close();
});

test('Failure to create a gain node does not leak its unconnected oscillator', async () => {
  const { instances } = audioFixture(); const alerts = createAlertSound();
  await alerts.unlock(); const context = instances[0];
  context.createGain = () => { throw new Error('Audio device unavailable'); };
  assert.doesNotThrow(() => alerts.play('message'));
  assert.equal(context.oscillators[0].disconnected, true);
  alerts.close();
});
