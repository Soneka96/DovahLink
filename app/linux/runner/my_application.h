#ifndef FLUTTER_MY_APPLICATION_H_
#define FLUTTER_MY_APPLICATION_H_

#include <gtk/gtk.h>

/// A GTK application that hosts the Flutter view.
G_DECLARE_FINAL_TYPE(MyApplication,
                     my_application,
                     MY,
                     APPLICATION,
                     GtkApplication)

/// Creates a new Flutter-based application.
MyApplication* my_application_new();

#endif  // FLUTTER_MY_APPLICATION_H_
