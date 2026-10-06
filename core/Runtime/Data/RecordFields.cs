using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace HomeCare.Core.Data
{
    /// <summary>
    /// 部屋・ポイント・タスクなどの1件を、「項目名 → 値」の単純な辞書に変換する（逆も）。
    /// 保存先（Firestoreなど）はこの辞書だけを扱うので、データの形を増やしても保存先の処理は変えずに済む。
    /// 値は string / long / double / bool / null / List&lt;object&gt; / Dictionary&lt;string, object&gt; のどれか。
    /// </summary>
    public static class RecordFields
    {
        public static Dictionary<string, object> ToFields(object record)
        {
            var fields = new Dictionary<string, object>();
            foreach (var field in FieldsOf(record.GetType()))
            {
                fields[field.Name] = ToValue(field.GetValue(record));
            }
            return fields;
        }

        /// <summary>
        /// 辞書から1件を作る。辞書に無い項目は初期値のまま。
        /// 新しい版のアプリが足した、知らない項目は無視する。
        /// </summary>
        public static T FromFields<T>(IDictionary<string, object> fields) where T : new() =>
            (T)FromFields(typeof(T), fields);

        /// <summary>2つの辞書（または値）の中身が同じか。</summary>
        public static bool AreEqual(object a, object b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }
            if (a is IDictionary<string, object> mapA && b is IDictionary<string, object> mapB)
            {
                return mapA.Count == mapB.Count
                    && mapA.All(pair => mapB.TryGetValue(pair.Key, out var other) && AreEqual(pair.Value, other));
            }
            if (a is IList listA && b is IList listB)
            {
                if (listA.Count != listB.Count)
                {
                    return false;
                }
                for (var i = 0; i < listA.Count; i++)
                {
                    if (!AreEqual(listA[i], listB[i]))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (IsNumber(a) && IsNumber(b))
            {
                return Convert.ToDouble(a, CultureInfo.InvariantCulture) == Convert.ToDouble(b, CultureInfo.InvariantCulture);
            }
            return a.Equals(b);
        }

        static IEnumerable<FieldInfo> FieldsOf(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        static object ToValue(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string text:
                    return text;
                case bool flag:
                    return flag;
                case int number:
                    return (long)number;
                case long number:
                    return number;
                case float number:
                    // 1.2f をそのまま double にすると 1.2000000476837158 になるので、見た目どおりの値にする
                    return double.Parse(number.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                case double number:
                    return number;
                case IList list:
                    return list.Cast<object>().Select(ToValue).ToList();
                default:
                    return ToFields(value);
            }
        }

        static object FromFields(Type type, IDictionary<string, object> fields)
        {
            var record = Activator.CreateInstance(type);
            foreach (var field in FieldsOf(type))
            {
                if (fields.TryGetValue(field.Name, out var value))
                {
                    field.SetValue(record, FromValue(field.FieldType, value));
                }
            }
            return record;
        }

        static object FromValue(Type type, object value)
        {
            if (value == null)
            {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }
            if (type == typeof(string))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            if (type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) || type == typeof(bool))
            {
                return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
            if (type.IsArray)
            {
                var items = (IList)value;
                var array = Array.CreateInstance(type.GetElementType(), items.Count);
                for (var i = 0; i < items.Count; i++)
                {
                    array.SetValue(FromValue(type.GetElementType(), items[i]), i);
                }
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var itemType = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(type);
                foreach (var item in (IList)value)
                {
                    list.Add(FromValue(itemType, item));
                }
                return list;
            }
            return FromFields(type, (IDictionary<string, object>)value);
        }

        static bool IsNumber(object value) =>
            value is int || value is long || value is float || value is double;
    }
}
