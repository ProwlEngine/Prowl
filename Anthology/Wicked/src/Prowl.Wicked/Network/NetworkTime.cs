// NetworkTime uses NetworkClient's snapshot interpolated timeline.
// This gives ideal results & ensures everything is on the same timeline.
// Some of the old NetworkTime code remains for ping time (rtt).
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Prowl.Wicked.Network.Messages;
using Prowl.Wicked.Tools;

namespace Prowl.Wicked.Network
{
    /// <summary>Synchronizes server time to clients.</summary>
    public static class NetworkTime
    {
        /// <summary>Ping message interval, used to calculate latency / RTT and predicted time.</summary>
        // 2s was enough to get a good average RTT.
        // for prediction, we want to react to latency changes more rapidly.
        const float DefaultPingInterval = 0.1f; // for resets
        public static float PingInterval = DefaultPingInterval;

        /// <summary>Average out the last few results from Ping</summary>
        // const because it's used immediately in _rtt constructor.
        public const int PingWindowSize = 50; // average over 50 * 100ms = 5s

        static double lastPingTime;

        static ExponentialMovingAverage _rtt = new ExponentialMovingAverage(PingWindowSize);

        // Stopwatch for accurate time measurement
        static readonly Stopwatch stopwatch = new Stopwatch();
        static double localFrameTime;

        static NetworkTime()
        {
            stopwatch.Start();
        }

        /// <summary>Returns double precision clock time _in this system_, unaffected by the network.</summary>
        public static double localTime => localFrameTime;

        /// <summary>The time in seconds since the server started.</summary>
        // via global NetworkClient snapshot interpolated timeline (if client).
        // on server, this is simply localTime.
        //
        // I measured the accuracy of float and I got this:
        // for the same day,  accuracy is better than 1 ms
        // after 1 day,  accuracy goes down to 7 ms
        // after 10 days, accuracy is 61 ms
        // after 30 days , accuracy is 238 ms
        // after 60 days, accuracy is 454 ms
        // in other words,  if the server is running for 2 months,
        // and you cast down to float,  then the time will jump in 0.4s intervals.
        public static double time
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => NetworkServer.Active
                ? localTime
                : NetworkClient.localTimeline;
        }

        // prediction //////////////////////////////////////////////////////////
        // NetworkTime.time is server time, behind by bufferTime.
        // for prediction, we want server time, ahead by latency.
        // so that client inputs at predictedTime=2 arrive on server at time=2.
        // the more accurate this is, the more closesly will corrections be
        // be applied and the less jitter we will see.
        //
        // we'll use a two step process to calculate predicted time:
        // 1. move snapshot interpolated time to server time, without being behind by bufferTime
        // 2. constantly send this time to server (included in ping message)
        //    server replies with how far off it was.
        //    client averages that offset and applies it to predictedTime to get ever closer.
        //
        // this is also very easy to test & verify:
        // - add LatencySimulation with 50ms latency
        // - log predictionError on server in OnServerPing, see if it gets closer to 0

        static int PredictionErrorWindowSize = 20; // average over 20 * 100ms = 2s
        static ExponentialMovingAverage _predictionErrorUnadjusted = new ExponentialMovingAverage(PredictionErrorWindowSize);
        public static double predictionErrorUnadjusted => _predictionErrorUnadjusted.Value;
        public static double predictionErrorAdjusted { get; private set; } // for debugging

        /// <summary>Predicted timeline in order for client inputs to be timestamped with the exact time when they will most likely arrive on the server. This is the basis for all prediction like PredictedRigidbody.</summary>
        // on client, this is based on localTime (aka Time.time) instead of the snapshot interpolated timeline.
        // this gives much better and immediately accurate results.
        // -> snapshot interpolation timeline tries to emulate a server timeline without hard offset corrections.
        // -> predictedTime does have hard offset corrections, so might as well use Time.time directly for this.
        //
        // note that predictedTime over unreliable is enough!
        // even with reliable components, it gives better results than if we were
        // to implemented predictedTime over reliable channel.
        public static double predictedTime
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => NetworkServer.Active
                ? localTime // server always uses it's own timeline
                : localTime + predictionErrorUnadjusted; // add the offset that the server told us we are off by
        }

        /// <summary>Clock difference in seconds between the client and the server. Always 0 on server.</summary>
        // original implementation used 'client - server' time. keep it this way.
        public static double offset => localTime - time;

        /// <summary>Round trip time (in seconds) that it takes a message to go client->server->client.</summary>
        public static double rtt => _rtt.Value;

        /// <Summary>Round trip time variance aka jitter, in seconds.</Summary>
        // "rttVariance" instead of "rttVar" for consistency with older versions.
        public static double rttVariance => _rtt.Variance;

        /// <summary>Round trip time standard deviation in seconds.</summary>
        public static double rttStandardDeviation => _rtt.StandardDeviation;

        public static void ResetStatics()
        {
            PingInterval = DefaultPingInterval;
            lastPingTime = 0;
            _rtt = new ExponentialMovingAverage(PingWindowSize);
            _predictionErrorUnadjusted = new ExponentialMovingAverage(PredictionErrorWindowSize);
            predictionErrorAdjusted = 0;
            stopwatch.Restart();
        }

        internal static void UpdateClient()
        {
            // localTime (double) instead of Time.time for accuracy over days
            if (localTime >= lastPingTime + PingInterval)
                SendPing();
        }

        // Separate method so we can call it from NetworkClient directly.
        internal static void SendPing()
        {
            // send raw predicted time without the offset applied yet.
            // we then apply the offset to it after.
            NetworkPingMessage pingMessage = new NetworkPingMessage(
                localTime,
                predictedTime
            );
            NetworkClient.Send(pingMessage);
            lastPingTime = localTime;
        }

        // client rtt calculation //////////////////////////////////////////////
        // executed at the server when we receive a ping message
        // reply with a pong containing the time from the client
        // and time from the server
        internal static void OnServerPing(NetworkConnection conn, NetworkPingMessage message)
        {
            // calculate the prediction offset that the client needs to apply to unadjusted time to reach server time.
            // this will be sent back to client for corrections.
            double unadjustedError = localTime - message.LocalTime;

            // to see how well the client's final prediction worked, compare with adjusted time.
            // this is purely for debugging.
            // >0 means: server is ... seconds ahead of client's prediction (good if small)
            // <0 means: server is ... seconds behind client's prediction.
            //           in other words, client is predicting too far ahead (not good)
            double adjustedError = localTime - message.PredictedTimeAdjusted;

            NetworkPongMessage pongMessage = new NetworkPongMessage(
                message.LocalTime,
                unadjustedError,
                adjustedError
            );
            NetworkServer.Send(conn, pongMessage);
        }

        // Executed at the client when we receive a Pong message
        // find out how long it took since we sent the Ping
        // and update time offset & prediction offset.
        internal static void OnClientPong(NetworkPongMessage message)
        {
            // prevent attackers from sending timestamps which are in the future
            if (message.LocalTime > localTime) return;

            // how long did this message take to come back
            double newRtt = localTime - message.LocalTime;
            _rtt.Add(newRtt);

            // feed unadjusted prediction error into our exponential moving average
            // store adjusted prediction error for debug / GUI purposes
            _predictionErrorUnadjusted.Add(message.PredictionErrorUnadjusted);
            predictionErrorAdjusted = message.PredictionErrorAdjusted;
        }

        // server rtt calculation //////////////////////////////////////////////
        // Executed at the client when we receive a ping message from the server.
        // in other words, this is for server sided ping + rtt calculation.
        // reply with a pong containing the time from the server
        internal static void OnClientPing(NetworkPingMessage message)
        {
            NetworkPongMessage pongMessage = new NetworkPongMessage(
                message.LocalTime,
                0, 0 // server doesn't predict
            );
            NetworkClient.Send(pongMessage);
        }

        // Executed at the server when we receive a Pong message back.
        // find out how long it took since we sent the Ping
        // and update time offset
        internal static void OnServerPong(NetworkConnection conn, NetworkPongMessage message)
        {
            // prevent attackers from sending timestamps which are in the future
            if (message.LocalTime > localTime) return;

            // how long did this message take to come back
            double newRtt = localTime - message.LocalTime;
            conn._rtt.Add(newRtt);
        }

        /// <summary>
        /// Called at the start of each frame to cache the current time.
        /// This ensures consistent time values throughout a frame.
        /// </summary>
        internal static void EarlyUpdate()
        {
            localFrameTime = stopwatch.Elapsed.TotalSeconds;
            // Note: localTimeline is now driven by snapshot interpolation in NetworkClient.UpdateTimeInterpolation
        }
    }
}
