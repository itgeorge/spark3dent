using System.Reflection.Emit;

namespace Cli;

class CliProgram
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Hello, World!");
        
        // Run in a loop until exit. 
        // - If args are provided, run based on them
        // - Otherwise, go into "interactive mode", prompt for command and parameters
        // Commands:
        // clients add (asks for client nickname, and address fields)
        // clients edit (asks for client nickname, fails if not found, then for, info and address fields, empty fields defaults to current value)
        // clients list (lists all clients, alphabetically sorted by nickname)
        // invoices issue <client nickname> <amount (handles both . and , separator)> [date (dd-MM-yyyy)] (validates amount, date is optional, defaults to today)
        // invoices correct <invoice number> <amount (handles both . and , separator)> [date (dd-MM-yyyy)] (validates number and date change consistent with other known invoices) 
        // invoices list (lists the latest invoices, sorted by date, newest first)
        // help (displays the help message)
        // exit (exits the program)
        
    }
}