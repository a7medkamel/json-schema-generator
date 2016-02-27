using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

public class Startup
{
    public async Task<object> Invoke(object dll_path)
    {
        var assembly = Assembly.LoadFrom(dll_path.ToString());

        return Helper.FindSchemas(assembly);
    }
}

public static class Helper
{
    public static IEnumerable<Type> Find(Assembly assembly) 
    {
        if (assembly == null) throw new ArgumentNullException("assembly");
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
    }

    public static IEnumerable<dynamic> FindSchemas(Assembly assembly, string ns = "Microsoft.BingAds.Api.Model")
    {
        var hash = new Dictionary<string, dynamic>();
        var queue = new Queue<Type>();

        var odataModels = Helper.Find(assembly)
                                    .Where(t => String.Equals(t.Namespace, ns, StringComparison.Ordinal))
                                    .Where(t => t.IsClass)
                                    ;
        
        odataModels.ToList().ForEach(queue.Enqueue);

        while (queue.Any())
        {
            var type = queue.Dequeue();

            if (!hash.ContainsKey(type.FullName))
            {
                var info = TypeInfo(type);
                
                hash.Add(type.FullName, new
                {
                    info.Name,
                    info.FullName,
                    info.BaseType,
                    info.Namespace,
                    info.Properties
                });

                ((IEnumerable<Type>)info.__DependantTypes)
                    .Where(t => !hash.ContainsKey(t.FullName))
                    .Where(t => t.IsClass)
                    .Where(t => !t.Namespace.StartsWith("System", StringComparison.Ordinal))
                    .ToList()
                    .ForEach(queue.Enqueue);
            }
        }
        
        return hash.Values;
    }

    public static dynamic TypeInfo(Type type)
    {
        //var properties = EntityProperty.PropertiesFor(type).ToArray();
        // var properties = Types.GetProperties(type); // todo [akamel] requires 'Microsoft.OData.Edm.dll' 
        //var properties = ((TypeInfo)type).DeclaredProperties.ToArray();
        var properties = EntityProperty.PropertiesFor(type).Select(p =>
        {
            var info = Objects.RefTypeInfoFor(p.PropertyType);

            return info != null? new
            {
                Info = p,
                info.IsNullable,
                info.IsEnumerable,
                info.Type
            } : null;
        })
        .Where(i => i!= null && i.Type != null && !i.Type.IsGenericParameter)
        .ToArray();

        return new {
            type.Name,
            type.FullName,
            type.Namespace,
            BaseType = type.BaseType.FullName,
            Properties = properties.Select(l => l).Select(i => new
            {
                i.Info.Name,
                i.IsNullable,
                i.IsEnumerable,
                Enum = i.Type.IsEnum? Enum.GetNames(i.Type) : null,
                TypeName = i.Type.Name,
                TypeNamespace = i.Type.Namespace,
                TypeFullName = i.Type.FullName,
                Attributes = i.Info.Attributes.Cast<Attribute>().Select(AttributeInfo).Where(a => a != null).ToArray()
            }).ToArray(),
            __DependantTypes = properties.Select(l => l.Type).Cast<Type>().ToArray()
        };
    }

    public static dynamic AttributeInfo(Attribute attr)
    {
        var type = attr.GetType();

        switch (type.Name)
        {
            case "KeyAttribute":

                break;
            default:
                return null;
        }

        return new
        {
            type.Name,
            type.FullName
        };
    }
}

public static class Types
{
    public static IEnumerable<PropertyDescriptor> GetProperties(Type type)
    {
        var metadata = new AssociatedMetadataTypeTypeDescriptionProvider(type).GetTypeDescriptor(type);

        //var members = def.GetProperties(BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.Instance).OrEmpty();
        var properties = metadata.GetProperties();
        var members = new PropertyDescriptor[properties.Count];
        properties.CopyTo(members, 0);

        return members;
    }
}

public static class Objects
{
    public static bool IsNullable(this Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }

    public static dynamic RefTypeInfoFor(Type type)
    {
        var isNullable = false;

        if (IsNullable(type))
        {
            isNullable = true;
            type = Nullable.GetUnderlyingType(type);
        }

        var info = TypeSystem.GetElementTypeInfo(type);

        //var code = Type.GetTypeCode(info.Type);
        //if (code == TypeCode.Object)
        //{
            return new {
                IsNullable = isNullable,
                info.IsEnumerable,
                info.Type
            };
        //}

        //return null;
    }

    //public static string RefNameFor(Type type)
    //{
    //    var info = RefTypeInfoFor(type);

    //    return info != null ? info.Type.Name : null;
    //}
}

public static class EntityProperty
{
    public static ICustomTypeDescriptor TypeDescriptorFor<T>()
    {
        return TypeDescriptorFor(typeof(T));
    }

    public static ICustomTypeDescriptor TypeDescriptorFor(Type type)
    {
        return new AssociatedMetadataTypeTypeDescriptionProvider(type).GetTypeDescriptor(type);
    }

    public static IEnumerable<PropertyDescriptor> PropertiesFor<T, TAttribute>()
    {
        return PropertiesForType<TAttribute>(typeof(T));
    }

    // TODO [akamel] refactor name
    public static IEnumerable<PropertyDescriptor> PropertiesForType<TAttribute>(Type type)
    {
        var descriptor = TypeDescriptorFor(type);
        if (descriptor != null)
        {
            // TODO [akamel] according to doc this line should work, but doesn't...
            //return descriptor.GetProperties(new[] { new KeyAttribute() }).OfType<PropertyDescriptor>();
            return descriptor.GetProperties().OfType<PropertyDescriptor>().Where(i => i.Attributes.OfType<TAttribute>().Count() != 0);
        }

        return Enumerable.Empty<PropertyDescriptor>();
    }

    public static IEnumerable<PropertyDescriptor> PropertiesFor(Type type)
    {
        var descriptor = TypeDescriptorFor(type);
        if (descriptor != null)
        {
            try
            {
                return descriptor.GetProperties().OfType<PropertyDescriptor>();
            }
            //catch (FileNotFoundException ex)
            //{
            //    return Enumerable.Empty<PropertyDescriptor>();
            //}
            catch (Exception ex)
            {
                return Enumerable.Empty<PropertyDescriptor>();
            }
        }

        return Enumerable.Empty<PropertyDescriptor>();
    }

    public static MemberExpression ExpressionFor<T>(string name)
    {
        return ExpressionFor<T>(Expression.Parameter(typeof(T)), name);
    }

    public static MemberExpression ExpressionFor<T>(Expression source, string name)
    {
        return Expression.Property(source, name);
    }

    public static Type TypeFor<T>(Expression expression)
    {
        if (expression.NodeType == ExpressionType.MemberAccess)
        {
            return ((MemberExpression)expression).Type;
        }

        return null;
    }
}

public static class TypeSystem
{
    public static dynamic GetElementTypeInfo(Type seqType)
    {
        Type ienum = FindIEnumerable(seqType);
        var isEnumerable = false;
        var retType = seqType;

        if (ienum != null)
        {
            isEnumerable = true;
            retType = ienum.GetGenericArguments()[0];
        }
        

        return new {
            IsEnumerable = isEnumerable,
            Type = retType
        };
    }

    public static Type GetElementType(Type seqType)
    {
        Type ienum = FindIEnumerable(seqType);
        if (ienum == null) return seqType;
        return ienum.GetGenericArguments()[0];
    }

    public static Type FindIEnumerable(Type seqType)
    {
        if (seqType == null || seqType == typeof(string))
            return null;
        if (seqType.IsArray)
            return typeof(IEnumerable<>).MakeGenericType(seqType.GetElementType());
        if (seqType.IsGenericType)
        {
            foreach (Type arg in seqType.GetGenericArguments())
            {
                Type ienum = typeof(IEnumerable<>).MakeGenericType(arg);
                if (ienum.IsAssignableFrom(seqType))
                {
                    return ienum;
                }
            }
        }

        Type[] ifaces = seqType.GetInterfaces();
        if (ifaces != null && ifaces.Length > 0)
        {
            foreach (Type iface in ifaces)
            {
                Type ienum = FindIEnumerable(iface);
                if (ienum != null) return ienum;
            }
        }
        if (seqType.BaseType != null && seqType.BaseType != typeof(object))
        {
            return FindIEnumerable(seqType.BaseType);
        }

        return null;
    }
}