const defaultTimeoutMs = 60000;

export class WorkerPool {
    constructor(scriptUrl, poolSize) {
        this.scriptUrl = scriptUrl;
        this.poolSize = poolSize;
        this.workers = [];
        this.freeWorkers = new Set();
        this.queue = [];
        this.pending = new Map();
        this._terminated = false;
        this._errorCounts = new Map();
        this._maxErrorsPerWorker = 3;

        for (let i = 0; i < poolSize; i++) {
            this._createWorker(i);
        }

        console.log(`[worker-pool] Created pool of ${poolSize} worker(s) from ${scriptUrl}`);
    }

    _createWorker(index) {
        const worker = new Worker(this.scriptUrl);
        worker.onmessage = (e) => this._onWorkerMessage(index, e);
        worker.onerror = (e) => this._onWorkerError(index, e);

        if (index < this.workers.length) {
            this.workers[index] = worker;
        } else {
            this.workers.push(worker);
        }
        this.freeWorkers.add(index);
    }

    execute(taskData, transferables, timeoutMs) {
        if (this._terminated) {
            return Promise.reject(new Error('[worker-pool] Pool has been terminated.'));
        }

        const timeout = timeoutMs !== undefined ? timeoutMs : defaultTimeoutMs;

        return new Promise((resolve, reject) => {
            this.queue.push({ taskData, transferables, resolve, reject, timeout });
            this.drainQueue();
        });
    }

    runTask(taskData, transferables, timeoutMs) {
        return this.execute(taskData, transferables, timeoutMs);
    }

    drainQueue() {
        while (this.queue.length > 0 && this.freeWorkers.size > 0) {
            const workerIndex = this.freeWorkers.values().next().value;
            this.freeWorkers.delete(workerIndex);

            const { taskData, transferables, resolve, reject, timeout } = this.queue.shift();

            let timeoutId = null;
            if (timeout > 0) {
                timeoutId = setTimeout(() => {
                    const callbacks = this.pending.get(workerIndex);
                    if (callbacks) {
                        this.pending.delete(workerIndex);
                        console.warn(`[worker-pool] Worker ${workerIndex} timed out after ${timeout}ms. Replacing worker.`);
                        this._replaceWorker(workerIndex);
                        callbacks.reject(new Error(`[worker-pool] Task timed out after ${timeout}ms.`));
                        this.drainQueue();
                    }
                }, timeout);
            }

            this.pending.set(workerIndex, { resolve, reject, timeoutId });

            try {
                if (transferables && transferables.length > 0) {
                    this.workers[workerIndex].postMessage(taskData, transferables);
                } else {
                    this.workers[workerIndex].postMessage(taskData);
                }
            } catch (err) {
                if (timeoutId !== null) clearTimeout(timeoutId);
                this.pending.delete(workerIndex);
                this.freeWorkers.add(workerIndex);
                reject(err);
            }
        }
    }

    _onWorkerMessage(workerIndex, event) {
        const callbacks = this.pending.get(workerIndex);
        if (callbacks) {
            if (callbacks.timeoutId !== null) clearTimeout(callbacks.timeoutId);
            this.pending.delete(workerIndex);
            this.freeWorkers.add(workerIndex);
            this._errorCounts.set(workerIndex, 0);

            if (event.data && event.data.error) {
                callbacks.reject(new Error(event.data.error));
            } else {
                callbacks.resolve(event.data);
            }
        } else {
            this.freeWorkers.add(workerIndex);
        }

        this.drainQueue();
    }

    _onWorkerError(workerIndex, event) {
        console.error(`[worker-pool] Worker ${workerIndex} error:`, event.message);

        const callbacks = this.pending.get(workerIndex);
        if (callbacks) {
            if (callbacks.timeoutId !== null) clearTimeout(callbacks.timeoutId);
            this.pending.delete(workerIndex);
            callbacks.reject(new Error(event.message || 'Worker error'));
        }

        // Track consecutive errors to prevent infinite crash-restart loops
        const count = (this._errorCounts.get(workerIndex) || 0) + 1;
        this._errorCounts.set(workerIndex, count);

        if (count <= this._maxErrorsPerWorker) {
            this._replaceWorker(workerIndex);
        } else {
            try { this.workers[workerIndex].terminate(); } catch (_) {}
            this.freeWorkers.delete(workerIndex);
            // Give up on this worker slot — terminate without replacing
            console.warn(`[worker-pool] Worker ${workerIndex} failed ${count} times. Not replacing.`);
        }

        this.drainQueue();
    }

    _replaceWorker(workerIndex) {
        try {
            this.workers[workerIndex].terminate();
        } catch (_) {}
        this.freeWorkers.delete(workerIndex);
        console.log(`[worker-pool] Replacing worker ${workerIndex} with a fresh instance.`);
        this._createWorker(workerIndex);
    }

    terminate() {
        this._terminated = true;

        for (const worker of this.workers) {
            try {
                worker.terminate();
            } catch (_) {}
        }
        this.workers.length = 0;

        for (const { reject } of this.queue) {
            reject(new Error('[worker-pool] Pool terminated.'));
        }
        this.queue.length = 0;

        for (const [, { reject, timeoutId }] of this.pending) {
            if (timeoutId !== null) clearTimeout(timeoutId);
            reject(new Error('[worker-pool] Pool terminated.'));
        }
        this.pending.clear();
        this.freeWorkers.clear();

        console.log('[worker-pool] Pool terminated.');
    }
}
