using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class ScoreScreenReplayLoadTests
    {
        [Test]
        public void ReadThenLoadChart_UsesReplayOutputsBeforeChartLoader()
        {
            var calls = new List<string>();
            var outputs = CreateOutputs("ProDrums");
            var seam = ProductionType("YARG.Menu.ScoreScreen.ScoreScreenReplayLoad");
            var method = seam.GetMethod("TryReadThenLoadChart");
            var replayType = outputs.GetType();
            var chartType = typeof(object);

            var result = InvokeGeneric(method, replayType, chartType,
                ReadReplay(true, outputs, calls, replayType),
                GetOutputs(outputs, calls, replayType),
                LoadChart(calls, "ProDrums"));

            Assert.That(GetProperty<bool>(result, "Success"), Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "read", "outputs", "load:ProDrums" }));
        }

        [Test]
        public void ReadThenLoadChart_DoesNotLoadChartWhenReplayReadFails()
        {
            var calls = new List<string>();
            var outputs = CreateOutputs("FourLaneDrums");
            var seam = ProductionType("YARG.Menu.ScoreScreen.ScoreScreenReplayLoad");
            var method = seam.GetMethod("TryReadThenLoadChart");
            var replayType = outputs.GetType();
            var result = InvokeGeneric(method, replayType, typeof(object),
                ReadReplay(false, outputs, calls, replayType),
                GetOutputs(outputs, calls, replayType),
                LoadChart(calls, "FourLaneDrums"));

            Assert.That(GetProperty<bool>(result, "Success"), Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "read" }));
        }

        private static Delegate ReadReplay(bool success, Array outputs, List<string> calls, Type replayType)
        {
            var resultType = typeof(ValueTuple<,>).MakeGenericType(typeof(bool), replayType);
            var parameter = Expression.Parameter(typeof(object), "ignored");
            var value = Expression.New(
                resultType.GetConstructor(new[] { typeof(bool), replayType }),
                Expression.Constant(success), Expression.Constant(outputs, replayType));
            var body = Expression.Block(
                Expression.Call(Expression.Constant(calls), typeof(List<string>).GetMethod("Add"),
                    Expression.Constant("read")), value);
            return Expression.Lambda(typeof(Func<>).MakeGenericType(resultType), body).Compile();
        }

        private static Delegate GetOutputs(Array outputs, List<string> calls, Type replayType)
        {
            var collectionType = typeof(IReadOnlyCollection<>).MakeGenericType(InstrumentType());
            var parameter = Expression.Parameter(replayType, "replay");
            var body = Expression.Block(
                Expression.Call(Expression.Constant(calls), typeof(List<string>).GetMethod("Add"),
                    Expression.Constant("outputs")),
                Expression.Constant(outputs, collectionType));
            return Expression.Lambda(typeof(Func<,>).MakeGenericType(replayType, collectionType), body, parameter)
                .Compile();
        }

        private static Delegate LoadChart(List<string> calls, string selectedOutput)
        {
            var collectionType = typeof(IReadOnlyCollection<>).MakeGenericType(InstrumentType());
            var parameter = Expression.Parameter(collectionType, "outputs");
            var body = Expression.Block(
                Expression.Call(Expression.Constant(calls), typeof(List<string>).GetMethod("Add"),
                    Expression.Constant("load:" + selectedOutput)),
                Expression.New(typeof(object)));
            return Expression.Lambda(typeof(Func<,>).MakeGenericType(collectionType, typeof(object)), body, parameter)
                .Compile();
        }

        private static Array CreateOutputs(string name)
        {
            var array = Array.CreateInstance(InstrumentType(), 1);
            array.SetValue(InstrumentValue(name), 0);
            return array;
        }

        private static object InvokeGeneric(MethodInfo method, Type replayType, Type chartType,
            Delegate readReplay, Delegate getOutputs, Delegate loadChart)
        {
            return method.MakeGenericMethod(replayType, chartType)
                .Invoke(null, new object[] { readReplay, getOutputs, loadChart });
        }

        private static Type ProductionType(string name)
        {
            var type = Type.GetType(name + ", Assembly-CSharp");
            if (type is null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type is not null) break;
                }
            }

            Assert.That(type, Is.Not.Null, $"Production type {name} is missing from loaded Unity assemblies.");
            return type;
        }

        private static Type InstrumentType() => ProductionType("YARG.Core.Instrument");

        private static object InstrumentValue(string name) => Enum.Parse(InstrumentType(), name);

        private static T GetProperty<T>(object value, string name) =>
            (T)value.GetType().GetProperty(name).GetValue(value);
    }
}
