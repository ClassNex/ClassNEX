using ClassNex.Models;

namespace ClassNex.Widgets;


public static class WidgetFactory
{
    public static WidgetBase? Create(WidgetConfig config) => config.Type switch
    {
        "date" => new DateWidget { Config = config },
        "clock" => new ClockWidget { Config = config },
        "schedule" => new ScheduleWidget { Config = config },
        "nextclass" => new NextClassWidget { Config = config },
        "countdown" => new CountdownWidget { Config = config },
        "text" => new TextWidget { Config = config },
        "divider" => new DividerWidget { Config = config },
        _ => null,
    };
}
