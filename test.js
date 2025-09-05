import Big from "https://cdn.jsdelivr.net/npm/big.js@7.0.1/big.min.js";
import { sleep } from 'k6';
import { Trend, Counter } from 'k6/metrics';
import { Httpx } from 'https://jslib.k6.io/httpx/0.1.0/index.js';
import { uuidv4 } from "https://jslib.k6.io/k6-utils/1.4.0/index.js";

const MAX_REQUESTS = __ENV.MAX_REQUESTS ?? 500;

export let options = {
   summaryTrendStats: [
      "p(99)",
      "count",
   ],
   scenarios: {
      payments: {
         exec: "payments",
         executor: "ramping-vus",
         startVUs: 1,
         gracefulRampDown: "0s",
         stages: [{ target: MAX_REQUESTS, duration: "60s" }],
      }
   },
   //executor: "ramping-vus",
   //startVUs: 1,
   //duration: '30s', // how long the test runs
};

const backendHttp = new Httpx({
    baseURL: "http://localhost:9999",
    headers: {
        "Content-Type": "application/json",
    },
    timeout: 1500,
});

let responseTime = new Trend('response_time', true);
const transactionsSuccessCounter = new Counter("transactions_success");
const transactionsFailureCounter = new Counter("transactions_failure");

const paymentRequestFixedAmount = new Big(19.90);

export async function payments() {
   const payload = {
      correlationId: uuidv4(),
      amount: paymentRequestFixedAmount.toNumber()
   };

   let res = await backendHttp.asyncPost('/payments', JSON.stringify(payload));

   // Record response time
   responseTime.add(res.timings.duration);

   if ([200, 201, 202, 204].includes(res.status)) {
      transactionsSuccessCounter.add(1);
      transactionsFailureCounter.add(0);
   } else {
      transactionsSuccessCounter.add(0);
      transactionsFailureCounter.add(1);
   }

   sleep(1);
}