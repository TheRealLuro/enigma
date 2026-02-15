def time_obj_to_ms(time_obj):
    return (
        time_obj["hours"] * 3600000 +
        time_obj["minutes"] * 60000 +
        time_obj["seconds"] * 1000 +
        time_obj["milliseconds"]
    )
