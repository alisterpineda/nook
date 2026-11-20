import { TestBed } from '@angular/core/testing';
import { HubConnectionBuilder } from '@microsoft/signalr';
import { ActivityHub, HUB_CONNECTION_BUILDER } from './activity-hub';

interface RetryPolicy {
  nextRetryDelayInMilliseconds(context: { previousRetryCount: number }): number | null;
}

/** What stands in for SignalR: the connections built, newest last, and how many of the next starts are refused. */
const signalr = {
  connections: [] as { starts: number; stops: number; retryPolicy: RetryPolicy }[],
  refusals: 0,
};

class FakeBuilder {
  private retryPolicy!: RetryPolicy;
  withUrl() {
    return this;
  }
  withAutomaticReconnect(retryPolicy: RetryPolicy) {
    this.retryPolicy = retryPolicy;
    return this;
  }
  build() {
    const connection = {
      starts: 0,
      stops: 0,
      retryPolicy: this.retryPolicy,
      start() {
        connection.starts++;
        if (signalr.refusals > 0) {
          signalr.refusals--;
          return Promise.reject(new Error('refused'));
        }
        return Promise.resolve();
      },
      stop() {
        connection.stops++;
        return Promise.resolve();
      },
      // The spec sends no hub messages, so the handlers are taken and never called.
      on: () => undefined,
      onreconnected: () => undefined,
    };
    signalr.connections.push(connection);
    return connection;
  }
}

describe('ActivityHub', () => {
  let hub: ActivityHub;
  let reconnects: number;

  beforeEach(() => {
    vi.useFakeTimers();
    signalr.connections.length = 0;
    signalr.refusals = 0;
    TestBed.configureTestingModule({
      providers: [
        {
          provide: HUB_CONNECTION_BUILDER,
          useValue: () => new FakeBuilder() as unknown as HubConnectionBuilder,
        },
      ],
    });
    hub = TestBed.inject(ActivityHub);
    reconnects = 0;
    hub.reconnected.subscribe(() => reconnects++);
  });

  afterEach(() => vi.useRealTimers());

  it('opens one connection however often it is started', async () => {
    await hub.start();
    await hub.start();

    expect(signalr.connections).toHaveLength(1);
    expect(signalr.connections[0].starts).toBe(1);
  });

  it('never gives up on a connection that dropped', async () => {
    await hub.start();

    const { retryPolicy } = signalr.connections[0];

    expect(retryPolicy.nextRetryDelayInMilliseconds({ previousRetryCount: 0 })).toBe(0);
    expect(retryPolicy.nextRetryDelayInMilliseconds({ previousRetryCount: 50 })).toBe(30000);
  });

  it('keeps trying a connection that could not be opened, and says when it has opened', async () => {
    signalr.refusals = 2;

    await expect(hub.start()).rejects.toThrow('refused');
    const [connection] = signalr.connections;
    expect(connection.starts).toBe(1);

    await vi.advanceTimersByTimeAsync(2000);
    expect(connection.starts).toBe(2);
    expect(reconnects).toBe(0);

    await vi.advanceTimersByTimeAsync(10000);
    expect(connection.starts).toBe(3);
    expect(reconnects).toBe(1);

    await vi.advanceTimersByTimeAsync(60000);
    expect(connection.starts).toBe(3);
    expect(signalr.connections).toHaveLength(1);
  });

  it('stops trying once stopped', async () => {
    signalr.refusals = 5;
    await expect(hub.start()).rejects.toThrow('refused');

    await hub.stop();
    await vi.advanceTimersByTimeAsync(60000);

    expect(signalr.connections[0].starts).toBe(1);
    expect(reconnects).toBe(0);
  });
});
