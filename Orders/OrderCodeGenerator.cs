namespace Orders;

public interface IOrderCodeGenerator
{
    string Generate(OrderDraft draft);
}
