using UnityEditor;
using UnityEngine;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Editor.Tests
{
    public static class SegmentSliceVerificationTests
    {
        [MenuItem("Tools/Tests/Run Slice Verification Tests")]
        public static void RunAllTests()
        {
            int passed = 0;
            int failed = 0;

            void Assert(bool condition, string testName)
            {
                if (condition)
                {
                    passed++;
                    Debug.Log($"<color=green>[PASS]</color> {testName}");
                }
                else
                {
                    failed++;
                    Debug.LogError($"<color=red>[FAIL]</color> {testName}");
                }
            }

            Debug.Log("=== Starting Slice Verification Tests ===");

            // 1. Crossing segments
            Vector2 a1 = new Vector2(0f, 0f), b1 = new Vector2(2f, 2f);
            Vector2 c1 = new Vector2(0f, 2f), d1 = new Vector2(2f, 0f);
            Assert(SutureThread.SegmentsIntersect(a1, b1, c1, d1), "1. Crossing Segments should intersect");

            // 2. Parallel segments
            Vector2 a2 = new Vector2(0f, 0f), b2 = new Vector2(0f, 5f);
            Vector2 c2 = new Vector2(1f, 0f), d2 = new Vector2(1f, 5f);
            Assert(!SutureThread.SegmentsIntersect(a2, b2, c2, d2), "2. Parallel Segments should not intersect");

            // 3. Collinear disjoint
            Vector2 a3 = new Vector2(0f, 0f), b3 = new Vector2(1f, 0f);
            Vector2 c3 = new Vector2(2f, 0f), d3 = new Vector2(3f, 0f);
            Assert(!SutureThread.SegmentsIntersect(a3, b3, c3, d3), "3. Collinear Disjoint should not intersect");

            // 4. Near miss (doesn't cross)
            Vector2 a4 = new Vector2(0f, 0f), b4 = new Vector2(2f, 0f);
            Vector2 c4 = new Vector2(1f, 0.1f), d4 = new Vector2(1f, 2f);
            Assert(!SutureThread.SegmentsIntersect(a4, b4, c4, d4), "4. Near-miss does not cross");

            // 5. Integration with SutureThread GameObject
            GameObject go1 = new GameObject("TestThread_1");
            SutureThread thread1 = go1.AddComponent<SutureThread>();
            Vector2[] waypoints1 = { new Vector2(0f, 0f), new Vector2(0f, 4f) };
            thread1.RenderPath(waypoints1);

            // Crossing slice
            Vector2 sliceStart = new Vector2(-1f, 2f);
            Vector2 sliceEnd = new Vector2(1f, 2f);
            Assert(thread1.TrySeverBySegment(sliceStart, sliceEnd, 0.2f), "5. SutureThread TrySeverBySegment crossing slice");

            // 6. Miss slice
            GameObject go2 = new GameObject("TestThread_2");
            SutureThread thread2 = go2.AddComponent<SutureThread>();
            Vector2[] waypoints2 = { new Vector2(0f, 0f), new Vector2(0f, 4f) };
            thread2.RenderPath(waypoints2);

            Vector2 missStart = new Vector2(5f, 5f);
            Vector2 missEnd = new Vector2(6f, 6f);
            Assert(!thread2.TrySeverBySegment(missStart, missEnd, 0.2f), "6. SutureThread TrySeverBySegment miss slice");

            // 7. Grazing touch slice (0.1m away, touchRadius = 0.2m)
            Vector2 grazeStart = new Vector2(0.1f, 1f);
            Vector2 grazeEnd = new Vector2(0.1f, 3f);
            Assert(thread2.TrySeverBySegment(grazeStart, grazeEnd, 0.2f), "7. SutureThread TrySeverBySegment grazing touch slice");

            // 8. Multi-cut: multiple threads crossed by one slice
            GameObject go3 = new GameObject("TestThread_3A");
            SutureThread thread3A = go3.AddComponent<SutureThread>();
            thread3A.RenderPath(new[] { new Vector2(1f, -2f), new Vector2(1f, 2f) });

            GameObject go4 = new GameObject("TestThread_3B");
            SutureThread thread3B = go4.AddComponent<SutureThread>();
            thread3B.RenderPath(new[] { new Vector2(2f, -2f), new Vector2(2f, 2f) });

            int multiSevered = SutureThread.SeverThreadsIntersectingSegment(new Vector2(0f, 0f), new Vector2(3f, 0f), 0.2f);
            Assert(multiSevered == 2, $"8. Multi-cut severed {multiSevered}/2 threads in one swipe");

            // Cleanup test GameObjects
            Object.DestroyImmediate(go1);
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(go3);
            Object.DestroyImmediate(go4);

            Debug.Log($"=== Slice Verification Tests Completed: {passed} Passed, {failed} Failed ===");
        }
    }
}
