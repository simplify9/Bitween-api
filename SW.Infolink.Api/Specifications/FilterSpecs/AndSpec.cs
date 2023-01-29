using System;
using System.Linq.Expressions;
using SW.PrimitiveTypes;

namespace SW.Infolink;

public class AndSpec : IPropertyMatchSpecification
{
    public IPropertyMatchSpecification Left { get; private set; }
    
    public IPropertyMatchSpecification Right { get; private set; }

    
    public AndSpec(IPropertyMatchSpecification left, IPropertyMatchSpecification right)
    {
        Left = left;
        Right = right;
    }


    public bool IsMatch(IPropertyReader reader) => Left.IsMatch(reader) && Right.IsMatch(reader);
    public string Name => "and";

    public override string ToString()
    {
        return $"({Left}) AND ({Right})";
    }
}