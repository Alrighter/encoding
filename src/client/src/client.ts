import { createClient, ConnectError, Code } from "@connectrpc/connect";
import { createGrpcTransport } from "@connectrpc/connect-node";
import { clone, fromJson, toJsonString, type JsonValue } from "@bufbuild/protobuf";
import { readFileSync } from "node:fs";
import { randomUUID } from "node:crypto";
import { OrderSchema, OrderService, type Order } from "./gen/orders_pb.js";

const client = createClient(OrderService, createGrpcTransport({ baseUrl: "http://localhost:5080" }));
const raw = JSON.parse(readFileSync("../../data/orders.json", "utf8")) as { [key: string]: JsonValue }[];
const orders = raw.map((o) => fromJson(OrderSchema, o, { ignoreUnknownFields: true }));
const sample = orders[raw.findIndex((o) => "comment" in o)];
const [mode, arg] = process.argv.slice(2);

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
const show = (order: Order | undefined) => (order ? toJsonString(OrderSchema, order) : "<none>");
const percentile = (sorted: number[], q: number) => sorted[Math.ceil(q * sorted.length) - 1];
const median = (values: number[]) => [...values].sort((a, b) => a - b)[Math.floor(values.length / 2)];

async function createMany(n: number) {
  const times: number[] = [];
  for (let i = 0; i < n; i++) {
    const start = performance.now();
    await client.createOrder({ requestId: randomUUID(), order: orders[i % orders.length] });
    times.push(performance.now() - start);
  }
  return times.sort((a, b) => a - b);
}

if (mode === "one") {
  const draft = clone(OrderSchema, sample);
  Object.assign(draft, { promoCode: "AUTUMN10" });
  console.log("sent:", show(draft));
  const { order } = await client.createOrder({ requestId: randomUUID(), order: draft });
  console.log("created:", show(order));
  await sleep(1000);
  const got = await client.getOrder({ orderId: order!.id });
  console.log("get:", show(got.order));
  const list = await client.getCustomerOrders({ customerId: order!.customerId });
  console.log(`customer ${order!.customerId} has ${list.orders.length} orders`);
} else if (mode === "bench") {
  await createMany(1000);
  const runs: number[][] = [];
  for (let r = 1; r <= 5; r++) {
    const times = await createMany(10000);
    const row = [0.5, 0.95, 0.99].map((q) => percentile(times, q));
    runs.push(row);
    console.log(`run ${r}: p50=${row[0].toFixed(3)} p95=${row[1].toFixed(3)} p99=${row[2].toFixed(3)} ms`);
  }
  const [p50, p95, p99] = [0, 1, 2].map((c) => median(runs.map((row) => row[c])));
  console.log(`median: p50=${p50.toFixed(3)} p95=${p95.toFixed(3)} p99=${p99.toFixed(3)} ms`);
} else if (mode === "send") {
  const n = Number(arg ?? 10000);
  await createMany(n);
  console.log(`sent ${n} orders`);
} else if (mode === "retry") {
  const order = clone(OrderSchema, sample);
  order.customerId = `TEST-${Date.now()}`;
  const requestId = randomUUID();
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      const res = await client.createOrder({ requestId, order }, { timeoutMs: 1000 });
      console.log(`attempt ${attempt}: OK, order ${res.order?.id}`);
      break;
    } catch (e) {
      console.log(`attempt ${attempt}: ${Code[ConnectError.from(e).code]}`);
    }
  }
  await sleep(3000);
  try {
    const list = await client.getCustomerOrders({ customerId: order.customerId });
    console.log(`server has ${list.orders.length} orders for ${order.customerId}: ${list.orders.map((o) => o.id).join(", ")}`);
  } catch (e) {
    console.log(`count failed: ${Code[ConnectError.from(e).code]}`);
  }
} else {
  console.log("usage: one | bench | send [n] | retry");
}
